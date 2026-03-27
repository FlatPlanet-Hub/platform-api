using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id);
    Task<Project?> GetByAppIdAsync(Guid appId);
    Task<IEnumerable<Project>> GetByAppIdsAsync(IEnumerable<Guid> appIds);
    Task<IEnumerable<Project>> GetAllAsync();
    Task<Project> CreateAsync(Project project);
    Task UpdateAsync(Project project);
}
