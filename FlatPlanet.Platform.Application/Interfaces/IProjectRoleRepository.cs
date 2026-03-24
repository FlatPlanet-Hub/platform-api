using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IProjectRoleRepository
{
    Task<ProjectRole> CreateAsync(ProjectRole role);
    Task<ProjectRole?> GetByIdAsync(Guid projectId, Guid roleId);
    Task<ProjectRole?> GetByNameAsync(Guid projectId, string name);
    Task<IEnumerable<ProjectRole>> GetByProjectIdAsync(Guid projectId);
    Task UpdateAsync(ProjectRole role);
    Task DeleteAsync(Guid roleId);
}
