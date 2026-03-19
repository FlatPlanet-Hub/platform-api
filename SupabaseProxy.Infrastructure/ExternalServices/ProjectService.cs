using SupabaseProxy.Application.DTOs.Project;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;

namespace SupabaseProxy.Infrastructure.ExternalServices;

public sealed class ProjectService : IProjectService
{
    private readonly IProjectRepository _projectRepo;
    private readonly IUserRepository _userRepo;
    private readonly IDbProxyService _dbProxy;
    private readonly IAuditService _audit;

    public ProjectService(
        IProjectRepository projectRepo,
        IUserRepository userRepo,
        IDbProxyService dbProxy,
        IAuditService audit)
    {
        _projectRepo = projectRepo;
        _userRepo = userRepo;
        _dbProxy = dbProxy;
        _audit = audit;
    }

    public async Task<ProjectResponse> CreateProjectAsync(Guid userId, CreateProjectRequest request)
    {
        var shortId = Guid.NewGuid().ToString("N")[..8];
        var schemaName = $"project_{shortId}";

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            SchemaName = schemaName,
            OwnerId = userId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _projectRepo.CreateAsync(project);

        await _dbProxy.CreateSchemaAsync(schemaName);

        var defaultRoles = BuildDefaultRoles(created.Id);
        foreach (var role in defaultRoles)
            await _projectRepo.CreateRoleAsync(role);

        var ownerRole = defaultRoles.First(r => r.Name == "owner");
        await _projectRepo.AddMemberAsync(new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = created.Id,
            UserId = userId,
            ProjectRoleId = ownerRole.Id,
            Status = "active",
            JoinedAt = DateTime.UtcNow
        });

        await _audit.LogAsync(userId, created.Id, "project.created", "project",
            new { projectName = request.Name, schemaName });

        return await BuildProjectResponseAsync(created);
    }

    public async Task<IEnumerable<ProjectResponse>> GetUserProjectsAsync(Guid userId)
    {
        var projects = await _projectRepo.GetByUserIdAsync(userId);
        var responses = new List<ProjectResponse>();
        foreach (var p in projects)
            responses.Add(await BuildProjectResponseAsync(p));
        return responses;
    }

    public async Task<ProjectResponse> GetProjectAsync(Guid projectId, Guid userId)
    {
        var project = await GetProjectOrThrowAsync(projectId);
        await RequireMembershipAsync(projectId, userId);
        return await BuildProjectResponseAsync(project, includeMembers: true);
    }

    public async Task<ProjectResponse> UpdateProjectAsync(Guid projectId, Guid userId, UpdateProjectRequest request)
    {
        var project = await GetProjectOrThrowAsync(projectId);
        await RequirePermissionAsync(projectId, userId, "manage_members");

        if (request.Name is not null) project.Name = request.Name;
        if (request.Description is not null) project.Description = request.Description;
        if (request.GitHubRepo is not null) project.GitHubRepo = request.GitHubRepo;
        project.UpdatedAt = DateTime.UtcNow;

        await _projectRepo.UpdateAsync(project);
        await _audit.LogAsync(userId, projectId, "project.updated", "project");
        return await BuildProjectResponseAsync(project);
    }

    public async Task DeactivateProjectAsync(Guid projectId, Guid userId)
    {
        var project = await GetProjectOrThrowAsync(projectId);
        await RequirePermissionAsync(projectId, userId, "delete_project");

        project.IsActive = false;
        project.UpdatedAt = DateTime.UtcNow;
        await _projectRepo.UpdateAsync(project);
        await _audit.LogAsync(userId, projectId, "project.deactivated", "project");
    }

    public async Task InviteMemberAsync(Guid projectId, Guid requestingUserId, InviteUserRequest request)
    {
        await RequirePermissionAsync(projectId, requestingUserId, "manage_members");

        var targetUser = await _userRepo.GetByGitHubUsernameAsync(request.GitHubUsername)
            ?? throw new KeyNotFoundException($"User '{request.GitHubUsername}' not found.");

        var existing = await _projectRepo.GetMemberAsync(projectId, targetUser.Id);
        if (existing is not null)
            throw new InvalidOperationException("User is already a member of this project.");

        var role = await _projectRepo.GetRoleByNameAsync(projectId, request.Role)
            ?? throw new KeyNotFoundException($"Role '{request.Role}' does not exist on this project.");

        await _projectRepo.AddMemberAsync(new ProjectMember
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

        var member = await _projectRepo.GetMemberAsync(projectId, targetUserId)
            ?? throw new KeyNotFoundException("Member not found.");

        var role = await _projectRepo.GetRoleByNameAsync(projectId, request.Role)
            ?? throw new KeyNotFoundException($"Role '{request.Role}' does not exist on this project.");

        member.ProjectRoleId = role.Id;
        await _projectRepo.UpdateMemberAsync(member);
        await _audit.LogAsync(requestingUserId, projectId, "member.role_updated", "project_members",
            new { targetUserId, newRole = request.Role });
    }

    public async Task RemoveMemberAsync(Guid projectId, Guid targetUserId, Guid requestingUserId)
    {
        await RequirePermissionAsync(projectId, requestingUserId, "manage_members");
        await _projectRepo.RemoveMemberAsync(projectId, targetUserId);
        await _audit.LogAsync(requestingUserId, projectId, "member.removed", "project_members",
            new { targetUserId });
    }

    public async Task<IEnumerable<ProjectMemberResponse>> GetMembersAsync(Guid projectId, Guid userId)
    {
        await RequireMembershipAsync(projectId, userId);
        var members = await _projectRepo.GetMembersAsync(projectId);
        var responses = new List<ProjectMemberResponse>();

        foreach (var m in members)
        {
            var user = await _userRepo.GetByIdAsync(m.UserId);
            var role = await _projectRepo.GetRoleAsync(projectId, m.ProjectRoleId);
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

    public async Task<IEnumerable<ProjectRoleResponse>> GetProjectRolesAsync(Guid projectId, Guid userId)
    {
        await RequireMembershipAsync(projectId, userId);
        var roles = await _projectRepo.GetRolesAsync(projectId);
        return roles.Select(r => new ProjectRoleResponse
        {
            Id = r.Id,
            Name = r.Name,
            Permissions = r.Permissions,
            IsDefault = r.IsDefault
        });
    }

    public async Task<ProjectRoleResponse> CreateProjectRoleAsync(Guid projectId, Guid userId, CreateProjectRoleRequest request)
    {
        await RequirePermissionAsync(projectId, userId, "manage_members");

        var existing = await _projectRepo.GetRoleByNameAsync(projectId, request.Name);
        if (existing is not null)
            throw new InvalidOperationException($"Role '{request.Name}' already exists.");

        var role = new ProjectRole
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = request.Name,
            Permissions = request.Permissions,
            IsDefault = false,
            CreatedAt = DateTime.UtcNow
        };

        var created = await _projectRepo.CreateRoleAsync(role);
        return new ProjectRoleResponse { Id = created.Id, Name = created.Name, Permissions = created.Permissions, IsDefault = created.IsDefault };
    }

    public async Task UpdateProjectRoleAsync(Guid projectId, Guid roleId, Guid userId, UpdateProjectRoleRequest request)
    {
        await RequirePermissionAsync(projectId, userId, "manage_members");
        var role = await _projectRepo.GetRoleAsync(projectId, roleId)
            ?? throw new KeyNotFoundException("Role not found.");

        if (request.Permissions is not null) role.Permissions = request.Permissions;
        await _projectRepo.UpdateRoleAsync(role);
    }

    public async Task DeleteProjectRoleAsync(Guid projectId, Guid roleId, Guid userId)
    {
        await RequirePermissionAsync(projectId, userId, "manage_members");
        var role = await _projectRepo.GetRoleAsync(projectId, roleId)
            ?? throw new KeyNotFoundException("Role not found.");
        if (role.IsDefault)
            throw new InvalidOperationException("Default roles cannot be deleted.");
        await _projectRepo.DeleteRoleAsync(roleId);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Project> GetProjectOrThrowAsync(Guid projectId) =>
        await _projectRepo.GetByIdAsync(projectId)
        ?? throw new KeyNotFoundException($"Project {projectId} not found.");

    private async Task RequireMembershipAsync(Guid projectId, Guid userId)
    {
        var member = await _projectRepo.GetMemberAsync(projectId, userId);
        if (member is null)
            throw new UnauthorizedAccessException("You are not a member of this project.");
    }

    private async Task RequirePermissionAsync(Guid projectId, Guid userId, string permission)
    {
        var member = await _projectRepo.GetMemberAsync(projectId, userId)
            ?? throw new UnauthorizedAccessException("You are not a member of this project.");
        var role = await _projectRepo.GetRoleAsync(projectId, member.ProjectRoleId)
            ?? throw new UnauthorizedAccessException("Your project role could not be found.");
        if (!role.Permissions.Contains(permission))
            throw new UnauthorizedAccessException($"You do not have '{permission}' permission on this project.");
    }

    private async Task<ProjectResponse> BuildProjectResponseAsync(Project project, bool includeMembers = false)
    {
        IEnumerable<ProjectMemberResponse>? members = null;
        if (includeMembers)
            members = await GetMembersAsync(project.Id, project.OwnerId);

        return new ProjectResponse
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            SchemaName = project.SchemaName,
            OwnerId = project.OwnerId,
            GitHubRepo = project.GitHubRepo,
            IsActive = project.IsActive,
            CreatedAt = project.CreatedAt,
            Members = members
        };
    }

    private static List<ProjectRole> BuildDefaultRoles(Guid projectId)
    {
        var now = DateTime.UtcNow;
        return
        [
            new ProjectRole { Id = Guid.NewGuid(), ProjectId = projectId, Name = "owner",     Permissions = ["read", "write", "ddl", "manage_members", "delete_project"], IsDefault = true, CreatedAt = now },
            new ProjectRole { Id = Guid.NewGuid(), ProjectId = projectId, Name = "developer", Permissions = ["read", "write", "ddl"], IsDefault = true, CreatedAt = now },
            new ProjectRole { Id = Guid.NewGuid(), ProjectId = projectId, Name = "viewer",    Permissions = ["read"], IsDefault = true, CreatedAt = now }
        ];
    }
}
