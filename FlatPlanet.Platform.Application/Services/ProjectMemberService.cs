using FlatPlanet.Platform.Application.DTOs.Project;
using FlatPlanet.Platform.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ProjectMemberService : IProjectMemberService
{
    private readonly ISecurityPlatformService _securityPlatform;
    private readonly IGitHubRepoService _gitHubRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly IApiTokenRepository _apiTokenRepo;
    private readonly ILogger<ProjectMemberService> _logger;

    public ProjectMemberService(
        ISecurityPlatformService securityPlatform,
        IGitHubRepoService gitHubRepo,
        IProjectRepository projectRepo,
        IApiTokenRepository apiTokenRepo,
        ILogger<ProjectMemberService> logger)
    {
        _securityPlatform = securityPlatform;
        _gitHubRepo = gitHubRepo;
        _projectRepo = projectRepo;
        _apiTokenRepo = apiTokenRepo;
        _logger = logger;
    }

    public async Task<IEnumerable<ProjectMemberResponse>> GetMembersAsync(Guid projectId, Guid userId)
    {
        var project = await GetOrThrowAsync(projectId);

        var appAccess = await _securityPlatform.GetUserAppAccessAsync(userId);
        var canViewAll = appAccess.Any(a =>
            a.AppSlug.Equals("dashboard-hub", StringComparison.OrdinalIgnoreCase) &&
            (a.RoleName.Equals("platform_owner", StringComparison.OrdinalIgnoreCase) ||
             a.Permissions.Contains("view_all_projects", StringComparer.OrdinalIgnoreCase)));

        if (!canViewAll && project.AppSlug is not null)
        {
            var allowed = await _securityPlatform.AuthorizeAsync(project.AppSlug, projectId.ToString(), "read");
            if (!allowed) throw new UnauthorizedAccessException("You do not have read access to this project.");
        }

        if (project.AppId is null) return [];

        var members = await _securityPlatform.GetAppMembersAsync(project.AppId.Value);
        var responses = new List<ProjectMemberResponse>();

        foreach (var m in members)
        {
            var spUser = await _securityPlatform.GetUserAsync(m.UserId);
            responses.Add(new ProjectMemberResponse
            {
                UserId = m.UserId,
                GitHubUsername = null,  // SP has no GitHub field (known limitation)
                Email = spUser.Email,
                FullName = spUser.FullName,
                RoleName = m.RoleName,
                Permissions = m.Permissions,
                GrantedAt = m.GrantedAt
            });
        }

        return responses;
    }

    public async Task InviteMemberAsync(Guid projectId, Guid requestingUserId, InviteUserRequest request)
    {
        var project = await GetOrThrowAsync(projectId);
        await RequirePermissionAsync(project, "manage_members", requestingUserId);

        if (project.AppId is null)
            throw new InvalidOperationException("Project is not registered in the Security Platform.");

        await _securityPlatform.GrantRoleAsync(project.AppId.Value, request.UserId, request.Role);

        // Auto-grant viewer on dashboard-hub — do not downgrade existing role
        try
        {
            var dashboardAppId = await _securityPlatform.GetAppIdBySlugAsync("dashboard-hub");
            if (dashboardAppId is not null)
            {
                var access = await _securityPlatform.GetUserAppAccessAsync(request.UserId);
                var hasRole = access.Any(a =>
                    a.AppSlug == "dashboard-hub" && !string.IsNullOrEmpty(a.RoleName));
                if (!hasRole)
                    await _securityPlatform.GrantRoleAsync(dashboardAppId.Value, request.UserId, "viewer");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-grant dashboard-hub viewer for user {UserId}", request.UserId);
        }

        if (!string.IsNullOrWhiteSpace(project.GitHubRepo) && !string.IsNullOrWhiteSpace(request.GitHubUsername))
        {
            var githubPermission = MapRoleToGitHub(request.Role);
            await _gitHubRepo.InviteCollaboratorAsync(project.GitHubRepo, request.GitHubUsername, githubPermission);
        }
    }

    public async Task RemoveMemberAsync(Guid projectId, Guid targetUserId, Guid requestingUserId)
    {
        var project = await GetOrThrowAsync(projectId);
        await RequirePermissionAsync(project, "manage_members", requestingUserId);

        if (project.AppId is null)
            throw new InvalidOperationException("Project is not registered in the Security Platform.");

        await _securityPlatform.RevokeRoleAsync(project.AppId.Value, targetUserId);

        // GitHub removal not possible — GitHubUsername not available from SP (known limitation)

        var tokens = await _apiTokenRepo.GetActiveByUserIdAsync(targetUserId);
        foreach (var t in tokens.Where(t => t.AppId == project.AppId))
            await _apiTokenRepo.RevokeAsync(t.Id, "member_removed");
    }

    public async Task UpdateMemberRoleAsync(Guid projectId, Guid targetUserId, Guid requestingUserId, UpdateMemberRoleRequest request)
    {
        var project = await GetOrThrowAsync(projectId);
        await RequirePermissionAsync(project, "manage_members", requestingUserId);

        if (project.AppId is null)
            throw new InvalidOperationException("Project is not registered in the Security Platform.");

        await _securityPlatform.ChangeRoleAsync(project.AppId.Value, targetUserId, request.Role);

        // GitHub permission not updated — GitHubUsername not available from SP (known limitation)
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Domain.Entities.Project> GetOrThrowAsync(Guid projectId) =>
        await _projectRepo.GetByIdAsync(projectId)
        ?? throw new KeyNotFoundException($"Project {projectId} not found.");

    private async Task RequirePermissionAsync(Domain.Entities.Project project, string permission, Guid requestingUserId = default)
    {
        if (project.AppSlug is null) return;

        if (requestingUserId != default)
        {
            var appAccess = await _securityPlatform.GetUserAppAccessAsync(requestingUserId);
            var canViewAll = appAccess.Any(a =>
                a.AppSlug.Equals("dashboard-hub", StringComparison.OrdinalIgnoreCase) &&
                (a.RoleName.Equals("platform_owner", StringComparison.OrdinalIgnoreCase) ||
                 a.Permissions.Contains("view_all_projects", StringComparer.OrdinalIgnoreCase)));
            if (canViewAll) return;
        }

        var allowed = await _securityPlatform.AuthorizeAsync(project.AppSlug, project.Id.ToString(), permission);
        if (!allowed) throw new UnauthorizedAccessException($"You do not have '{permission}' permission on this project.");
    }

    private static string MapRoleToGitHub(string role) => role.ToLowerInvariant() switch
    {
        "owner"     => "admin",
        "developer" => "push",
        _           => "pull"
    };
}
