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
    Task<(string AppServiceName, string AppServiceUrl)> ProvisionAsync(
        string appServiceName,
        AppServiceEnvVars envVars);
}
