using SupabaseProxy.Application.DTOs.Project;

namespace SupabaseProxy.Application.Interfaces;

public interface IProjectService
{
    Task<ProjectResponse> CreateProjectAsync(Guid userId, CreateProjectRequest request);
    Task<IEnumerable<ProjectResponse>> GetUserProjectsAsync(Guid userId);
    Task<ProjectResponse> GetProjectAsync(Guid projectId, Guid userId);
    Task<ProjectResponse> UpdateProjectAsync(Guid projectId, Guid userId, UpdateProjectRequest request);
    Task DeactivateProjectAsync(Guid projectId, Guid userId);

    Task InviteMemberAsync(Guid projectId, Guid requestingUserId, InviteUserRequest request);
    Task UpdateMemberRoleAsync(Guid projectId, Guid targetUserId, Guid requestingUserId, UpdateMemberRoleRequest request);
    Task RemoveMemberAsync(Guid projectId, Guid targetUserId, Guid requestingUserId);
    Task<IEnumerable<ProjectMemberResponse>> GetMembersAsync(Guid projectId, Guid userId);

    Task<IEnumerable<ProjectRoleResponse>> GetProjectRolesAsync(Guid projectId, Guid userId);
    Task<ProjectRoleResponse> CreateProjectRoleAsync(Guid projectId, Guid userId, CreateProjectRoleRequest request);
    Task UpdateProjectRoleAsync(Guid projectId, Guid roleId, Guid userId, UpdateProjectRoleRequest request);
    Task DeleteProjectRoleAsync(Guid projectId, Guid roleId, Guid userId);
}
