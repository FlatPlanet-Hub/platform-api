using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IResourceRepository
{
    Task<Resource> CreateAsync(Resource resource);
    Task<Resource?> GetByIdAsync(Guid id);
    Task<Resource?> GetByIdentifierAsync(Guid appId, string identifier);
    Task<IEnumerable<Resource>> GetByAppIdAsync(Guid appId);
    Task<IEnumerable<ResourceType>> GetAllTypesAsync();
    Task UpdateAsync(Resource resource);
    Task DeactivateAsync(Guid id);
}
