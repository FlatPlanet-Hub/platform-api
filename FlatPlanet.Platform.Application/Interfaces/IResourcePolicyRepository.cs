using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IResourcePolicyRepository
{
    Task<IEnumerable<ResourcePolicy>> GetByResourceIdAsync(Guid resourceId);
}
