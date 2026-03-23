using FlatPlanet.Platform.Application.DTOs.Project;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IProjectMemberService
{
    Task InviteMemberAsync(Guid projectId, Guid requestingUserId, InviteUserRequest request);
    Task UpdateMemberRoleAsync(Guid projectId, Guid targetUserId, Guid requestingUserId, UpdateMemberRoleRequest request);
    Task RemoveMemberAsync(Guid projectId, Guid targetUserId, Guid requestingUserId);
    Task<IEnumerable<ProjectMemberResponse>> GetMembersAsync(Guid projectId, Guid userId);
}
