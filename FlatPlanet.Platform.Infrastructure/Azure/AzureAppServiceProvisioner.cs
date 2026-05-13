using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using FlatPlanet.Platform.Application.DTOs.Azure;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Platform.Infrastructure.Azure;

public sealed class AzureAppServiceProvisioner : IAzureAppServiceProvisioner
{
    private readonly AzureSettings _azure;
    private readonly SupabaseSettings _supabase;
    private readonly ILogger<AzureAppServiceProvisioner> _logger;
    private readonly DefaultAzureCredential _credential;
    private readonly ArmClient _armClient;

    public AzureAppServiceProvisioner(
        IOptions<AzureSettings> azureOptions,
        IOptions<SupabaseSettings> supabaseOptions,
        ILogger<AzureAppServiceProvisioner> logger)
    {
        _azure      = azureOptions.Value;
        _supabase   = supabaseOptions.Value;
        _logger     = logger;
        _credential = new DefaultAzureCredential();
        _armClient  = new ArmClient(_credential, _azure.SubscriptionId);
    }

    public async Task<(string AppServiceName, string AppServiceUrl, string PublishProfileXml)> ProvisionAsync(
        string appServiceName,
        AppServiceEnvVars envVars)
    {
        var rgResourceId = global::Azure.Core.ResourceIdentifier.Parse(
            $"/subscriptions/{_azure.SubscriptionId}/resourceGroups/{_azure.ResourceGroupName}");
        var resourceGroup = _armClient.GetResourceGroupResource(rgResourceId);

        var webSiteCollection = resourceGroup.GetWebSites();

        var siteData = new WebSiteData(new global::Azure.Core.AzureLocation(_azure.Location))
        {
            AppServicePlanId = global::Azure.Core.ResourceIdentifier.Parse(_azure.AppServicePlanResourceId),
            SiteConfig = new SiteConfigProperties
            {
                // FPPlatform uses a Linux App Service Plan — must use LinuxFxVersion.
                // WindowsFxVersion / NetFrameworkVersion are Windows-only and must NOT be set on Linux.
                LinuxFxVersion = "DOTNETCORE|10.0",
                IsAlwaysOn = false,
            },
            IsHttpsOnly = true,
        };

        WebSiteResource site;
        try
        {
            var operation = await webSiteCollection.CreateOrUpdateAsync(
                global::Azure.WaitUntil.Completed,
                appServiceName,
                siteData);
            site = operation.Value;
        }
        catch (global::Azure.RequestFailedException ex) when (ex.Status == 409)
        {
            throw new InvalidOperationException($"App Service name '{appServiceName}' is already taken in Azure.");
        }
        catch (global::Azure.RequestFailedException ex)
        {
            throw new Exception(ex.Message);
        }

        // Build app settings
        var appSettings = new Dictionary<string, string>
        {
            // Required for Linux App Service — .NET binds :5000 by default but Azure probes :8080.
            // Without this the app crash-loops on every cold start (230s timeout).
            ["ASPNETCORE_URLS"]            = "http://0.0.0.0:8080",
            ["ASPNETCORE_ENVIRONMENT"]     = "Production",
            ["Jwt__SecretKey"]             = envVars.JwtSecretKey,
            ["Jwt__Issuer"]                = envVars.JwtIssuer,
            ["Jwt__Audience"]              = envVars.JwtAudience,
            ["PlatformApi__BaseUrl"]       = envVars.PlatformApiBaseUrl,
            ["ConnectionStrings__Default"] = BuildConnectionString(envVars.SchemaName),
        };

        if (envVars.PlatformApiToken is not null)
            appSettings["PlatformApi__Token"] = envVars.PlatformApiToken;

        if (envVars.AllowedOrigins is not null)
            appSettings["Cors__AllowedOrigins__0"] = envVars.AllowedOrigins;

        var appSettingsData = new AppServiceConfigurationDictionary();
        foreach (var kv in appSettings)
            appSettingsData.Properties[kv.Key] = kv.Value;

        try
        {
            await site.UpdateApplicationSettingsAsync(appSettingsData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update application settings for App Service '{AppServiceName}'", appServiceName);
            throw new Exception($"App Service '{appServiceName}' was created but application settings could not be applied: {ex.Message}");
        }

        // Refresh to get the actual DefaultHostName Azure assigned (includes random suffix + region).
        // WaitUntil.Completed guarantees the resource exists, but DefaultHostName may still be null
        // on the create response in some regions — a fresh GET is more reliable.
        var refreshed = await site.GetAsync();
        var hostName = refreshed.Value.Data.DefaultHostName;

        if (string.IsNullOrWhiteSpace(hostName))
        {
            _logger.LogWarning("Azure did not return a DefaultHostName for '{AppServiceName}'. Falling back to constructed URL.", appServiceName);
            hostName = $"{appServiceName}.azurewebsites.net";
        }

        var url = $"https://{hostName}";
        _logger.LogInformation("Provisioned Azure App Service '{AppServiceName}' at {Url}", appServiceName, url);

        // Fetch publish profile XML for GitHub Actions secret
        string publishProfileXml;
        try
        {
            var profileResponse = await site.GetPublishingProfileXmlWithSecretsAsync(new CsmPublishingProfile());
            using var reader = new System.IO.StreamReader(profileResponse.Value);
            publishProfileXml = await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch publish profile for '{AppServiceName}' — CI/CD secret will not be set", appServiceName);
            publishProfileXml = string.Empty;
        }

        return (appServiceName, url, publishProfileXml);
    }

    public async Task<string> GetPublishProfileAsync(string appServiceName)
    {
        try
        {
            var siteResourceId = global::Azure.Core.ResourceIdentifier.Parse(
                $"/subscriptions/{_azure.SubscriptionId}/resourceGroups/{_azure.ResourceGroupName}/providers/Microsoft.Web/sites/{appServiceName}");
            var site = _armClient.GetWebSiteResource(siteResourceId);

            var profileResponse = await site.GetPublishingProfileXmlWithSecretsAsync(new CsmPublishingProfile());
            using var reader = new System.IO.StreamReader(profileResponse.Value);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch publish profile for '{AppServiceName}'", appServiceName);
            return string.Empty;
        }
    }

    public async Task UpdateCorsOriginAsync(string appServiceName, string allowedOrigin)
    {
        var siteResourceId = global::Azure.Core.ResourceIdentifier.Parse(
            $"/subscriptions/{_azure.SubscriptionId}/resourceGroups/{_azure.ResourceGroupName}/providers/Microsoft.Web/sites/{appServiceName}");
        var site = _armClient.GetWebSiteResource(siteResourceId);

        // GET existing settings so we don't clobber anything
        var existing = await site.GetApplicationSettingsAsync();
        var merged   = new AppServiceConfigurationDictionary();
        foreach (var kv in existing.Value.Properties)
            merged.Properties[kv.Key] = kv.Value;

        // Add / replace the CORS origin
        merged.Properties["Cors__AllowedOrigins__0"] = allowedOrigin;

        await site.UpdateApplicationSettingsAsync(merged);

        _logger.LogInformation(
            "Updated Cors__AllowedOrigins__0 on App Service '{AppServiceName}' → {Origin}",
            appServiceName, allowedOrigin);
    }

    public async Task UpdateAppSettingAsync(string appServiceName, string key, string value)
    {
        var siteResourceId = global::Azure.Core.ResourceIdentifier.Parse(
            $"/subscriptions/{_azure.SubscriptionId}/resourceGroups/{_azure.ResourceGroupName}/providers/Microsoft.Web/sites/{appServiceName}");
        var site = _armClient.GetWebSiteResource(siteResourceId);

        // GET existing settings so we don't clobber anything
        var existing = await site.GetApplicationSettingsAsync();
        var merged   = new AppServiceConfigurationDictionary();
        foreach (var kv in existing.Value.Properties)
            merged.Properties[kv.Key] = kv.Value;

        // Add / replace the target setting
        merged.Properties[key] = value;

        await site.UpdateApplicationSettingsAsync(merged);

        _logger.LogInformation(
            "Updated app setting '{Key}' on App Service '{AppServiceName}'",
            key, appServiceName);
    }

    private string BuildConnectionString(string schemaName) =>
        $"Host={_supabase.Host};Port={_supabase.Port};Database={_supabase.Database};" +
        $"Username={_supabase.AdminUser};Password={_supabase.AdminPassword};" +
        $"Search Path={schemaName};" +
        "SSL Mode=Require;Trust Server Certificate=true;No Reset On Close=true;" +
        "Minimum Pool Size=0;Maximum Pool Size=5;Keepalive=30";
}
