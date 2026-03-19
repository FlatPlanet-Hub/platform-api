using SupabaseProxy.Domain.Entities;

namespace SupabaseProxy.Application.Interfaces;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id);
    Task<IEnumerable<Project>> GetByUserIdAsync(Guid userId);
    Task<Project> CreateAsync(Project project);
    Task UpdateAsync(Project project);

    Task<ProjectRole> CreateRoleAsync(ProjectRole role);
    Task<ProjectRole?> GetRoleAsync(Guid projectId, Guid roleId);
    Task<ProjectRole?> GetRoleByNameAsync(Guid projectId, string name);
    Task<IEnumerable<ProjectRole>> GetRolesAsync(Guid projectId);
    Task UpdateRoleAsync(ProjectRole role);
    Task DeleteRoleAsync(Guid roleId);

    Task<ProjectMember?> GetMemberAsync(Guid projectId, Guid userId);
    Task<IEnumerable<ProjectMember>> GetMembersAsync(Guid projectId);
    Task AddMemberAsync(ProjectMember member);
    Task UpdateMemberAsync(ProjectMember member);
    Task RemoveMemberAsync(Guid projectId, Guid userId);
}
