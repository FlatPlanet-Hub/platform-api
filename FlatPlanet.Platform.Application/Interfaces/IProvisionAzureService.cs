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
}
