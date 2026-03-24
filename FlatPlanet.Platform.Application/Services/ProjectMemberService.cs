using FlatPlanet.Platform.Application.DTOs.Project;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ProjectMemberService : IProjectMemberService
{
    private readonly IProjectMemberRepository _memberRepo;
    private readonly IProjectRoleRepository _roleRepo;
    private readonly IUserRepository _userRepo;
    private readonly IAuditService _audit;

    public ProjectMemberService(
        IProjectMemberRepository memberRepo,
        IProjectRoleRepository roleRepo,
        IUserRepository userRepo,
        IAuditService audit)
    {
        _memberRepo = memberRepo;
        _roleRepo = roleRepo;
        _userRepo = userRepo;
        _audit = audit;
    }

    public async Task<IEnumerable<ProjectMemberResponse>> GetMembersAsync(Guid projectId, Guid userId)
    {
        await RequireMembershipAsync(projectId, userId);
        var members = await _memberRepo.GetByProjectIdAsync(projectId);
        var responses = new List<ProjectMemberResponse>();

        foreach (var m in members)
        {
            var user = await _userRepo.GetByIdAsync(m.UserId);
            var role = await _roleRepo.GetByIdAsync(projectId, m.ProjectRoleId);
            if (user is null || role is null) continue;

            responses.Add(new ProjectMemberResponse
            {
                UserId = m.UserId,
                GitHubUsername = user.GitHubUsername,
                FirstName = user.FirstName,
                LastName = user.LastName,
                AvatarUrl = user.AvatarUrl,
                RoleName = role.Name,
                Permissions = role.Permissions,
                JoinedAt = m.JoinedAt
            });
        }

        return responses;
    }

    public async Task InviteMemberAsync(Guid projectId, Guid requestingUserId, InviteUserRequest request)
    {
        await RequirePermissionAsync(projectId, requestingUserId, "manage_members");

        var targetUser = await _userRepo.GetByGitHubUsernameAsync(request.GitHubUsername)
            ?? throw new KeyNotFoundException($"User '{request.GitHubUsername}' not found.");

        var existing = await _memberRepo.GetAsync(projectId, targetUser.Id);
        if (existing is not null)
            throw new InvalidOperationException("User is already a member of this project.");

        var role = await _roleRepo.GetByNameAsync(projectId, request.Role)
            ?? throw new KeyNotFoundException($"Role '{request.Role}' does not exist on this project.");

        await _memberRepo.AddAsync(new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            UserId = targetUser.Id,
            ProjectRoleId = role.Id,
            InvitedBy = requestingUserId,
            Status = "active",
            JoinedAt = DateTime.UtcNow
        });

        await _audit.LogAsync(requestingUserId, projectId, "member.invited", "project_members",
            new { invitedUserId = targetUser.Id, role = request.Role });
    }

    public async Task UpdateMemberRoleAsync(Guid projectId, Guid targetUserId, Guid requestingUserId, UpdateMemberRoleRequest request)
    {
        await RequirePermissionAsync(projectId, requestingUserId, "manage_members");

        var member = await _memberRepo.GetAsync(projectId, targetUserId)
            ?? throw new KeyNotFoundException("Member not found.");

        var role = await _roleRepo.GetByNameAsync(projectId, request.Role)
            ?? throw new KeyNotFoundException($"Role '{request.Role}' does not exist on this project.");

        member.ProjectRoleId = role.Id;
        await _memberRepo.UpdateAsync(member);
        await _audit.LogAsync(requestingUserId, projectId, "member.role_updated", "project_members",
            new { targetUserId, newRole = request.Role });
    }

    public async Task RemoveMemberAsync(Guid projectId, Guid targetUserId, Guid requestingUserId)
    {
        await RequirePermissionAsync(projectId, requestingUserId, "manage_members");
        await _memberRepo.RemoveAsync(projectId, targetUserId);
        await _audit.LogAsync(requestingUserId, projectId, "member.removed", "project_members",
            new { targetUserId });
    }

    private async Task RequireMembershipAsync(Guid projectId, Guid userId)
    {
        var member = await _memberRepo.GetAsync(projectId, userId);
        if (member is null)
            throw new UnauthorizedAccessException("You are not a member of this project.");
    }

    private async Task RequirePermissionAsync(Guid projectId, Guid userId, string permission)
    {
        var member = await _memberRepo.GetAsync(projectId, userId)
            ?? throw new UnauthorizedAccessException("You are not a member of this project.");
        var role = await _roleRepo.GetByIdAsync(projectId, member.ProjectRoleId)
            ?? throw new UnauthorizedAccessException("Your project role could not be found.");
        if (!role.Permissions.Contains(permission))
            throw new UnauthorizedAccessException($"You do not have '{permission}' permission on this project.");
    }
}
