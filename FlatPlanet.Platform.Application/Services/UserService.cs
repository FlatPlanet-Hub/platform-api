using FlatPlanet.Platform.Application.DTOs.Auth;
using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Services;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _userRepo;
    private readonly IRoleRepository _roleRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly IProjectMemberRepository _memberRepo;
    private readonly IProjectRoleRepository _roleRepoProject;
    private readonly ICustomRoleRepository _customRoleRepo;
    private readonly IAuditService _audit;
    private readonly IEncryptionService _encryption;
    private readonly IUserAppRoleRepository _userAppRoleRepo;
    private readonly IAppRepository _appRepo;
    private readonly IRolePermissionRepository _rolePermRepo;

    public UserService(
        IUserRepository userRepo,
        IRoleRepository roleRepo,
        IProjectRepository projectRepo,
        IProjectMemberRepository memberRepo,
        IProjectRoleRepository roleRepoProject,
        ICustomRoleRepository customRoleRepo,
        IAuditService audit,
        IEncryptionService encryption,
        IUserAppRoleRepository userAppRoleRepo,
        IAppRepository appRepo,
        IRolePermissionRepository rolePermRepo)
    {
        _userRepo = userRepo;
        _roleRepo = roleRepo;
        _projectRepo = projectRepo;
        _memberRepo = memberRepo;
        _roleRepoProject = roleRepoProject;
        _customRoleRepo = customRoleRepo;
        _audit = audit;
        _encryption = encryption;
        _userAppRoleRepo = userAppRoleRepo;
        _appRepo = appRepo;
        _rolePermRepo = rolePermRepo;
    }

    public async Task<User> UpsertFromGitHubAsync(GitHubUserProfile profile)
    {
        var encryptedToken = _encryption.Encrypt(profile.AccessToken);
        var existing = await _userRepo.GetByGitHubIdAsync(profile.Id);

        if (existing is not null)
        {
            // Update GitHub-owned fields; preserve admin-set first_name/last_name
            existing.GitHubUsername = profile.Login;
            existing.Email ??= profile.Email;
            existing.AvatarUrl = profile.AvatarUrl;
            existing.GitHubAccessToken = encryptedToken;
            existing.UpdatedAt = DateTime.UtcNow;
            await _userRepo.UpdateAsync(existing);
            return existing;
        }

        // New user (not pre-onboarded): parse name from GitHub profile
        var (firstName, lastName) = ParseName(profile.Name ?? profile.Login);

        var newUser = new User
        {
            Id = Guid.NewGuid(),
            GitHubId = profile.Id,
            GitHubUsername = profile.Login,
            FirstName = firstName,
            LastName = lastName,
            Email = profile.Email,
            AvatarUrl = profile.AvatarUrl,
            GitHubAccessToken = encryptedToken,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _userRepo.CreateAsync(newUser);

        var userRole = await _roleRepo.GetByNameAsync("user");
        if (userRole is not null)
            await _userRepo.AssignSystemRoleAsync(created.Id, userRole.Id, created.Id);

        await _audit.LogAsync(created.Id, null, "user.registered", "user", new { githubUsername = profile.Login });

        return created;
    }

    public async Task<UserProfileResponse> GetProfileAsync(Guid userId)
    {
        var user = await _userRepo.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        var systemRoles = await _userRepo.GetSystemRolesAsync(userId);
        var projectSummaries = await GetUserProjectsForTokenAsync(userId);

        return new UserProfileResponse
        {
            Id = user.Id,
            GitHubUsername = user.GitHubUsername,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            AvatarUrl = user.AvatarUrl,
            SystemRoles = systemRoles,
            Projects = projectSummaries
        };
    }

    public async Task<IEnumerable<UserProjectSummaryDto>> GetUserProjectsForTokenAsync(Guid userId)
    {
        var projects = await _projectRepo.GetByUserIdAsync(userId);
        var summaries = new List<UserProjectSummaryDto>();

        foreach (var project in projects)
        {
            var member = await _memberRepo.GetAsync(project.Id, userId);
            if (member is null) continue;

            var role = await _roleRepoProject.GetByIdAsync(project.Id, member.ProjectRoleId);
            if (role is null) continue;

            summaries.Add(new UserProjectSummaryDto
            {
                ProjectId = project.Id,
                Name = project.Name,
                Schema = project.SchemaName,
                ProjectRole = role.Name,
                Permissions = role.Permissions
            });
        }

        return summaries;
    }

    public async Task<IEnumerable<IamAppClaims>> GetIamAppClaimsAsync(Guid userId)
    {
        var userRoles = (await _userAppRoleRepo.GetByUserAsync(userId))
            .Where(r => r.Status == "active" && (r.ExpiresAt is null || r.ExpiresAt > DateTime.UtcNow))
            .ToList();

        // Group by app
        var byApp = userRoles.GroupBy(r => r.AppId);
        var claims = new List<IamAppClaims>();

        foreach (var group in byApp)
        {
            var app = await _appRepo.GetByIdAsync(group.Key);
            if (app is null || app.Status != "active") continue;

            var roles = new List<string>();
            var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var ur in group)
            {
                var role = await _roleRepo.GetByIdAsync(ur.RoleId);
                if (role is not null) roles.Add(role.Name);
                var perms = await _rolePermRepo.GetPermissionNamesByRoleIdAsync(ur.RoleId);
                foreach (var p in perms) permissions.Add(p);
            }

            claims.Add(new IamAppClaims
            {
                AppId = app.Id.ToString(),
                AppSlug = app.Slug,
                Schema = app.SchemaName,
                Roles = roles,
                Permissions = permissions.ToList()
            });
        }

        return claims;
    }

    public async Task AssignSystemRoleAsync(Guid requestingUserId, RoleAssignRequest request)
    {
        var requesterRoles = await _userRepo.GetSystemRolesAsync(requestingUserId);
        if (!requesterRoles.Contains("platform_admin"))
            throw new UnauthorizedAccessException("Only platform admins can assign system roles.");

        var role = await _roleRepo.GetByNameAsync(request.RoleName)
            ?? throw new KeyNotFoundException($"Role '{request.RoleName}' not found.");

        await _userRepo.AssignSystemRoleAsync(request.UserId, role.Id, requestingUserId);
        await _audit.LogAsync(requestingUserId, null, "role.assigned", "user_roles",
            new { targetUserId = request.UserId, roleName = request.RoleName });
    }

    public async Task RevokeSystemRoleAsync(Guid requestingUserId, RoleRevokeRequest request)
    {
        var requesterRoles = await _userRepo.GetSystemRolesAsync(requestingUserId);
        if (!requesterRoles.Contains("platform_admin"))
            throw new UnauthorizedAccessException("Only platform admins can revoke system roles.");

        var role = await _roleRepo.GetByNameAsync(request.RoleName)
            ?? throw new KeyNotFoundException($"Role '{request.RoleName}' not found.");

        await _userRepo.RevokeSystemRoleAsync(request.UserId, role.Id);
        await _audit.LogAsync(requestingUserId, null, "role.revoked", "user_roles",
            new { targetUserId = request.UserId, roleName = request.RoleName });
    }

    public async Task<IEnumerable<Role>> GetSystemRolesAsync() =>
        await _roleRepo.GetAllAsync();

    public async Task<IEnumerable<string>> GetEffectivePermissionsAsync(Guid userId, IEnumerable<string> systemRoles)
    {
        var permissions = new HashSet<string>();

        // platform_admin gets all admin permissions
        if (systemRoles.Contains("platform_admin"))
        {
            permissions.UnionWith(["manage_users", "manage_roles", "view_audit_log",
                                   "read", "write", "ddl", "manage_members", "delete_project"]);
        }

        // Union permissions from all assigned custom roles
        var customRoles = await _customRoleRepo.GetByUserIdAsync(userId);
        foreach (var role in customRoles)
            permissions.UnionWith(role.Permissions);

        return permissions;
    }

    private static (string? firstName, string? lastName) ParseName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => (null, null),
            1 => (parts[0], null),
            _ => (parts[0], parts[1])
        };
    }
}
