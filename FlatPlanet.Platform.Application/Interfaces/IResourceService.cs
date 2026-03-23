using FlatPlanet.Platform.Application.DTOs.Iam;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IResourceService
{
    Task<ResourceDto> CreateAsync(Guid appId, CreateResourceRequest request);
    Task<IEnumerable<ResourceDto>> GetByAppIdAsync(Guid appId);
    Task<ResourceDto?> GetByIdAsync(Guid id);
    Task<ResourceDto> UpdateAsync(Guid appId, Guid id, UpdateResourceRequest request);
    Task DeactivateAsync(Guid appId, Guid id);
    Task<IEnumerable<ResourceTypeDto>> GetTypesAsync();
}
