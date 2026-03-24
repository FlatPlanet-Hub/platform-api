using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IProjectMemberRepository
{
    Task<ProjectMember?> GetAsync(Guid projectId, Guid userId);
    Task<IEnumerable<ProjectMember>> GetByProjectIdAsync(Guid projectId);
    Task AddAsync(ProjectMember member);
    Task UpdateAsync(ProjectMember member);
    Task RemoveAsync(Guid projectId, Guid userId);
}
