using FlatPlanet.Platform.Application.DTOs.Azure;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IProvisionAzureService
{
    Task<ProvisionAzureResponse> ProvisionAsync(
        Guid projectId,
        Guid userId,
        string userEmail,
        string hubBaseUrl);

    Task<SyncGitHubActionsResponse> SyncGitHubActionsAsync(
        Guid projectId,
        Guid userId,
        string userEmail);
}
