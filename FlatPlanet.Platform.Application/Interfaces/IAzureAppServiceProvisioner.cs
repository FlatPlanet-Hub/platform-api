using FlatPlanet.Platform.Application.DTOs.Azure;

namespace FlatPlanet.Platform.Application.Interfaces;

/// <summary>
/// Provisions an Azure App Service inside the configured resource group using Managed Identity.
/// </summary>
public interface IAzureAppServiceProvisioner
{
    /// <summary>
    /// Creates an Azure App Service and sets all standard environment variables.
    /// Returns (AppServiceName, AppServiceUrl) on success.
    /// Throws InvalidOperationException if name is already taken in Azure (maps to 409).
    /// Throws Exception with Azure error message for all other ARM failures (maps to 500).
    /// </summary>
    Task<(string AppServiceName, string AppServiceUrl, string PublishProfileXml)> ProvisionAsync(
        string appServiceName,
        AppServiceEnvVars envVars);

    /// <summary>
    /// Fetches the publishing profile XML for an already-provisioned App Service.
    /// Returns an empty string if the profile cannot be retrieved.
    /// </summary>
    Task<string> GetPublishProfileAsync(string appServiceName);

    /// <summary>
    /// Merges (or replaces) the Cors__AllowedOrigins__0 app setting on an already-provisioned
    /// App Service without touching any other settings.
    /// </summary>
    Task UpdateCorsOriginAsync(string appServiceName, string allowedOrigin);

    /// <summary>
    /// Merges (or replaces) a single app setting key/value on an already-provisioned
    /// App Service without touching any other settings.
    /// </summary>
    Task UpdateAppSettingAsync(string appServiceName, string key, string value);
}
