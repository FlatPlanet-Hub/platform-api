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

public sealed class AzureAppServiceProvisioner(
    IOptions<AzureSettings> azureOptions,
    IOptions<SupabaseSettings> supabaseOptions,
    ILogger<AzureAppServiceProvisioner> logger) : IAzureAppServiceProvisioner
{
    private readonly AzureSettings _azure = azureOptions.Value;
    private readonly SupabaseSettings _supabase = supabaseOptions.Value;

    public async Task<(string AppServiceName, string AppServiceUrl)> ProvisionAsync(
        string appServiceName,
        AppServiceEnvVars envVars)
    {
        var credential = new DefaultAzureCredential();
        var armClient = new ArmClient(credential, _azure.SubscriptionId);

        var rgResourceId = global::Azure.Core.ResourceIdentifier.Parse(
            $"/subscriptions/{_azure.SubscriptionId}/resourceGroups/{_azure.ResourceGroupName}");
        var resourceGroup = armClient.GetResourceGroupResource(rgResourceId);

        var webSiteCollection = resourceGroup.GetWebSites();

        var siteData = new WebSiteData(new global::Azure.Core.AzureLocation(_azure.Location))
        {
            AppServicePlanId = global::Azure.Core.ResourceIdentifier.Parse(_azure.AppServicePlanResourceId),
            SiteConfig = new SiteConfigProperties
            {
                NetFrameworkVersion = "v10.0",
                // WindowsFxVersion is required for Windows App Service plans.
                // If migrating to Linux, change to LinuxFxVersion = "DOTNET|10.0".
                WindowsFxVersion = "DOTNET|10.0",
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
            ["Jwt__SecretKey"]             = envVars.JwtSecretKey,
            ["Jwt__Issuer"]                = envVars.JwtIssuer,
            ["Jwt__Audience"]              = envVars.JwtAudience,
            ["PlatformApi__BaseUrl"]       = envVars.PlatformApiBaseUrl,
            ["ConnectionStrings__Default"] = BuildConnectionString(envVars.SchemaName),
        };

        if (envVars.PlatformApiToken is not null)
            appSettings["PlatformApi__Token"] = envVars.PlatformApiToken;

        var appSettingsData = new AppServiceConfigurationDictionary();
        foreach (var kv in appSettings)
            appSettingsData.Properties[kv.Key] = kv.Value;

        try
        {
            await site.UpdateApplicationSettingsAsync(appSettingsData);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update application settings for App Service '{AppServiceName}'", appServiceName);
            throw new Exception($"App Service '{appServiceName}' was created but application settings could not be applied: {ex.Message}");
        }

        var url = $"https://{appServiceName}.azurewebsites.net";
        logger.LogInformation("Provisioned Azure App Service '{AppServiceName}' at {Url}", appServiceName, url);

        return (appServiceName, url);
    }

    private string BuildConnectionString(string schemaName) =>
        $"Host={_supabase.Host};Port={_supabase.Port};Database={_supabase.Database};" +
        $"Username={_supabase.AdminUser};Password={_supabase.AdminPassword};" +
        $"Search Path={schemaName};" +
        "SSL Mode=Require;Trust Server Certificate=true;No Reset On Close=true;" +
        "Minimum Pool Size=0;Maximum Pool Size=5;Keepalive=30";
}
