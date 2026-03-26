using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id);
    Task<IEnumerable<Project>> GetByUserIdAsync(Guid userId);
    Task<Project?> GetByAppSlugAsync(string slug);
    Task<Project> CreateAsync(Project project);
    Task UpdateAsync(Project project);
}
