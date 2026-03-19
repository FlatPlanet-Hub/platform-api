using Microsoft.Extensions.Options;
using SupabaseProxy.Application.DTOs.Auth;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;
using SupabaseProxy.Infrastructure.Common.Helpers;
using SupabaseProxy.Infrastructure.Configuration;

namespace SupabaseProxy.Infrastructure.ExternalServices;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _userRepo;
    private readonly IRoleRepository _roleRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly IAuditService _audit;
    private readonly EncryptionSettings _encryption;

    public UserService(
        IUserRepository userRepo,
        IRoleRepository roleRepo,
        IProjectRepository projectRepo,
        IAuditService audit,
        IOptions<EncryptionSettings> encryption)
    {
        _userRepo = userRepo;
        _roleRepo = roleRepo;
        _projectRepo = projectRepo;
        _audit = audit;
        _encryption = encryption.Value;
    }

    public async Task<User> UpsertFromGitHubAsync(GitHubUserProfile profile)
    {
        var encryptedToken = EncryptionHelper.Encrypt(profile.AccessToken, _encryption.Key);
        var existing = await _userRepo.GetByGitHubIdAsync(profile.Id);

        if (existing is not null)
        {
            existing.GitHubUsername = profile.Login;
            existing.Email = profile.Email;
            existing.DisplayName = profile.Name ?? profile.Login;
            existing.AvatarUrl = profile.AvatarUrl;
            existing.GitHubAccessToken = encryptedToken;
            existing.UpdatedAt = DateTime.UtcNow;
            await _userRepo.UpdateAsync(existing);
            return existing;
        }

        var newUser = new User
        {
            Id = Guid.NewGuid(),
            GitHubId = profile.Id,
            GitHubUsername = profile.Login,
            Email = profile.Email,
            DisplayName = profile.Name ?? profile.Login,
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
            DisplayName = user.DisplayName,
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
            var member = await _projectRepo.GetMemberAsync(project.Id, userId);
            if (member is null) continue;

            var role = await _projectRepo.GetRoleAsync(project.Id, member.ProjectRoleId);
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
}
