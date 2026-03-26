using FlatPlanet.Platform.Application.DTOs.Project;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ProjectMemberService : IProjectMemberService
{
    private readonly ISecurityPlatformService _securityPlatform;
    private readonly IGitHubRepoService _gitHubRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly IApiTokenRepository _apiTokenRepo;

    public ProjectMemberService(
        ISecurityPlatformService securityPlatform,
        IGitHubRepoService gitHubRepo,
        IProjectRepository projectRepo,
        IApiTokenRepository apiTokenRepo)
    {
        _securityPlatform = securityPlatform;
        _gitHubRepo = gitHubRepo;
        _projectRepo = projectRepo;
        _apiTokenRepo = apiTokenRepo;
    }

    public async Task<IEnumerable<ProjectMemberResponse>> GetMembersAsync(Guid projectId, Guid userId)
    {
        var project = await GetOrThrowAsync(projectId);
        if (project.AppSlug is not null)
        {
            var allowed = await _securityPlatform.AuthorizeAsync(project.AppSlug, projectId.ToString(), "read", userId);
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
                GitHubUsername = spUser.GitHubUsername,
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
        await RequirePermissionAsync(project, requestingUserId, "manage_members");

        if (project.AppId is null)
            throw new InvalidOperationException("Project is not registered in the Security Platform.");

        await _securityPlatform.GrantRoleAsync(project.AppId.Value, request.UserId, request.Role);

        var spUser = await _securityPlatform.GetUserAsync(request.UserId);
        if (!string.IsNullOrWhiteSpace(project.GitHubRepo) && !string.IsNullOrWhiteSpace(spUser.GitHubUsername))
        {
            var githubPermission = MapRoleToGitHub(request.Role);
            await _gitHubRepo.InviteCollaboratorAsync(project.GitHubRepo, spUser.GitHubUsername, githubPermission);
        }
    }

    public async Task RemoveMemberAsync(Guid projectId, Guid targetUserId, Guid requestingUserId)
    {
        var project = await GetOrThrowAsync(projectId);
        await RequirePermissionAsync(project, requestingUserId, "manage_members");

        if (project.AppId is null)
            throw new InvalidOperationException("Project is not registered in the Security Platform.");

        await _securityPlatform.RevokeRoleAsync(project.AppId.Value, targetUserId);

        var spUser = await _securityPlatform.GetUserAsync(targetUserId);
        if (!string.IsNullOrWhiteSpace(project.GitHubRepo) && !string.IsNullOrWhiteSpace(spUser.GitHubUsername))
            await _gitHubRepo.RemoveCollaboratorAsync(project.GitHubRepo, spUser.GitHubUsername);

        var tokens = await _apiTokenRepo.GetActiveByUserIdAsync(targetUserId);
        foreach (var t in tokens.Where(t => t.AppId == project.AppId))
            await _apiTokenRepo.RevokeAsync(t.Id, "member_removed");
    }

    public async Task UpdateMemberRoleAsync(Guid projectId, Guid targetUserId, Guid requestingUserId, UpdateMemberRoleRequest request)
    {
        var project = await GetOrThrowAsync(projectId);
        await RequirePermissionAsync(project, requestingUserId, "manage_members");

        if (project.AppId is null)
            throw new InvalidOperationException("Project is not registered in the Security Platform.");

        await _securityPlatform.RevokeRoleAsync(project.AppId.Value, targetUserId);
        await _securityPlatform.GrantRoleAsync(project.AppId.Value, targetUserId, request.Role);

        var spUser = await _securityPlatform.GetUserAsync(targetUserId);
        if (!string.IsNullOrWhiteSpace(project.GitHubRepo) && !string.IsNullOrWhiteSpace(spUser.GitHubUsername))
        {
            var githubPermission = MapRoleToGitHub(request.Role);
            await _gitHubRepo.InviteCollaboratorAsync(project.GitHubRepo, spUser.GitHubUsername, githubPermission);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Domain.Entities.Project> GetOrThrowAsync(Guid projectId) =>
        await _projectRepo.GetByIdAsync(projectId)
        ?? throw new KeyNotFoundException($"Project {projectId} not found.");

    private async Task RequirePermissionAsync(Domain.Entities.Project project, Guid userId, string permission)
    {
        if (project.AppSlug is null) return;
        var allowed = await _securityPlatform.AuthorizeAsync(project.AppSlug, project.Id.ToString(), permission, userId);
        if (!allowed) throw new UnauthorizedAccessException($"You do not have '{permission}' permission on this project.");
    }

    private static string MapRoleToGitHub(string role) => role.ToLowerInvariant() switch
    {
        "owner" => "admin",
        "developer" => "push",
        _ => "pull"
    };
}
