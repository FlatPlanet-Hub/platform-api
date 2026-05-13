using FlatPlanet.Platform.Application.DTOs.Azure;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IProvisionAzureService
{
    Task<ProvisionAzureResponse> ProvisionAsync(
        Guid projectId,
        Guid userId,
        string userEmail,
        string hubBaseUrl,
        string? appServiceName = null);

    Task<SyncGitHubActionsResponse> SyncGitHubActionsAsync(
        Guid projectId,
        Guid userId,
        string userEmail);

    Task<SyncCorsResponse> SyncCorsAsync(Guid projectId, Guid userId);

    /// <summary>
    /// Pushes the new platform API token to the project's Azure App Service app settings.
    /// Returns immediately (no-op) if the project has no App Service provisioned yet.
    /// </summary>
    Task SyncTokenAsync(Guid projectId, string newToken);
}
