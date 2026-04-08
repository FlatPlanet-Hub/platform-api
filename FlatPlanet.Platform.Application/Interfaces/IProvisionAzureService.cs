using FlatPlanet.Platform.Application.DTOs.Azure;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IProvisionAzureService
{
    Task<ProvisionAzureResponse> ProvisionAsync(
        Guid projectId,
        Guid userId,
        string hubBaseUrl);
}
