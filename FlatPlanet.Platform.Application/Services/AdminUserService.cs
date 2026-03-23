using System.ComponentModel.DataAnnotations;
using FlatPlanet.Platform.Application.DTOs.Admin;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Services;

public sealed class AdminUserService : IAdminUserService
{
    private readonly IUserRepository _userRepo;
    private readonly IProjectMemberRepository _memberRepo;
    private readonly IRoleRepository _roleRepo;
    private readonly ICustomRoleRepository _customRoleRepo;
    private readonly IAuditService _audit;
    private readonly IUserAppRoleRepository _userAppRoleRepo;

    public AdminUserService(
        IUserRepository userRepo,
        IProjectMemberRepository memberRepo,
        IRoleRepository roleRepo,
        ICustomRoleRepository customRoleRepo,
        IAuditService audit,
        IUserAppRoleRepository userAppRoleRepo)
    {
        _userRepo = userRepo;
        _memberRepo = memberRepo;
        _roleRepo = roleRepo;
        _customRoleRepo = customRoleRepo;
        _audit = audit;
        _userAppRoleRepo = userAppRoleRepo;
    }

    public async Task<AdminUserListResponse> ListUsersAsync(AdminUserListFilter filter)
    {
        var (users, total) = await _userRepo.ListAsync(filter);
        var dtos = await Task.WhenAll(users.Select(u => BuildAdminUserDtoAsync(u)));

        return new AdminUserListResponse
        {
            Users = dtos,
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<AdminUserDto> GetUserAsync(Guid userId)
    {
        var user = await _userRepo.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");
        return await BuildAdminUserDtoAsync(user);
    }

    public async Task<AdminUserDto> CreateUserAsync(Guid adminId, CreateAdminUserRequest request)
    {
        ValidateCreateRequest(request);

        var existing = await _userRepo.GetByGitHubIdAsync(request.GitHubId);
        if (existing is not null)
            throw new InvalidOperationException($"User with GitHub ID {request.GitHubId} already exists.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            GitHubId = request.GitHubId,
            GitHubUsername = request.GitHubUsername,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            AvatarUrl = request.AvatarUrl,
            IsActive = true,
            OnboardedBy = adminId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _userRepo.CreateAsync(user);

        // Assign default "user" system role
        var userRole = await _roleRepo.GetByNameAsync("user");
        if (userRole is not null)
            await _userRepo.AssignSystemRoleAsync(created.Id, userRole.Id, adminId);

        // Assign any additional roles from request
        foreach (var roleId in request.RoleIds)
        {
            var systemRole = await _roleRepo.GetByIdAsync(roleId);
            if (systemRole is not null)
                await _userRepo.AssignSystemRoleAsync(created.Id, roleId, adminId);
            else
            {
                var customRole = await _customRoleRepo.GetByIdAsync(roleId);
                if (customRole is not null)
                    await _customRoleRepo.AssignToUserAsync(created.Id, roleId, adminId);
            }
        }

        // Assign project memberships
        foreach (var assignment in request.ProjectAssignments)
        {
            var member = new ProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = assignment.ProjectId,
                UserId = created.Id,
                ProjectRoleId = assignment.ProjectRoleId,
                InvitedBy = adminId,
                Status = "active",
                JoinedAt = DateTime.UtcNow
            };
            await _memberRepo.AddAsync(member);
        }

        await _audit.LogAsync(adminId, null, "admin.user.created", "users",
            new { targetUserId = created.Id, githubUsername = created.GitHubUsername });

        return await BuildAdminUserDtoAsync(created);
    }

    public async Task<IEnumerable<AdminUserDto>> BulkCreateUsersAsync(Guid adminId, BulkCreateUsersRequest request)
    {
        var results = new List<AdminUserDto>();
        foreach (var userRequest in request.Users)
            results.Add(await CreateUserAsync(adminId, userRequest));
        return results;
    }

    public async Task<AdminUserDto> UpdateUserAsync(Guid adminId, Guid userId, UpdateAdminUserRequest request)
    {
        if (adminId == userId && request.IsActive == false)
            throw new InvalidOperationException("Admins cannot deactivate their own account.");

        var user = await _userRepo.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        if (request.FirstName is not null) user.FirstName = request.FirstName;
        if (request.LastName is not null) user.LastName = request.LastName;
        if (request.Email is not null)
        {
            ValidateEmail(request.Email);
            user.Email = request.Email;
        }
        if (request.IsActive.HasValue) user.IsActive = request.IsActive.Value;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepo.UpdateAsync(user);
        await _audit.LogAsync(adminId, null, "admin.user.updated", "users",
            new { targetUserId = userId });

        return await BuildAdminUserDtoAsync(user);
    }

    public async Task UpdateUserRolesAsync(Guid adminId, Guid userId, UpdateUserRolesRequest request)
    {
        if (adminId == userId)
            throw new InvalidOperationException("Admins cannot change their own roles.");

        var user = await _userRepo.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        // Separate system roles from custom roles
        var systemRoleIds = new List<Guid>();
        var customRoleIds = new List<Guid>();

        foreach (var roleId in request.RoleIds)
        {
            var systemRole = await _roleRepo.GetByIdAsync(roleId);
            if (systemRole is not null)
                systemRoleIds.Add(roleId);
            else
                customRoleIds.Add(roleId);
        }

        await _userRepo.SetSystemRolesAsync(userId, systemRoleIds, adminId);
        await _customRoleRepo.SetUserCustomRolesAsync(userId, customRoleIds, adminId);

        await _audit.LogAsync(adminId, null, "admin.user.roles_updated", "user_roles",
            new { targetUserId = userId, roleIds = request.RoleIds });
    }

    public async Task UpdateUserProjectRoleAsync(Guid adminId, Guid userId, Guid projectId, UpdateUserProjectRoleRequest request)
    {
        var member = await _memberRepo.GetAsync(projectId, userId)
            ?? throw new KeyNotFoundException($"User {userId} is not a member of project {projectId}.");

        member.ProjectRoleId = request.ProjectRoleId;
        await _memberRepo.UpdateAsync(member);

        await _audit.LogAsync(adminId, projectId, "admin.member.role_updated", "project_members",
            new { targetUserId = userId, newRoleId = request.ProjectRoleId });
    }

    public async Task DeactivateUserAsync(Guid adminId, Guid userId)
    {
        if (adminId == userId)
            throw new InvalidOperationException("Admins cannot deactivate their own account.");

        var user = await _userRepo.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepo.UpdateAsync(user);

        // Cascade: revoke all tokens
        await _userRepo.RevokeAllTokensAsync(userId);

        await _audit.LogAsync(adminId, null, "admin.user.deactivated", "users",
            new { targetUserId = userId });
    }

    public async Task UpdateUserStatusAsync(Guid adminId, Guid userId, UpdateUserStatusRequest request)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "active", "inactive", "suspended" };
        if (!allowed.Contains(request.Status))
            throw new ArgumentException($"Invalid status '{request.Status}'. Must be active, inactive, or suspended.");

        if (adminId == userId && request.Status != "active")
            throw new InvalidOperationException("Admins cannot deactivate or suspend their own account.");

        var user = await _userRepo.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        user.Status = request.Status;
        user.IsActive = request.Status == "active";
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepo.UpdateAsync(user);

        // Cascade on deactivate/suspend: revoke tokens (sessions expire naturally)
        if (request.Status != "active")
            await _userRepo.RevokeAllTokensAsync(userId);

        await _audit.LogAsync(adminId, null, "admin.user.status_updated", "users",
            new { targetUserId = userId, status = request.Status });
    }

    public async Task UpdateUserAppRoleAsync(Guid adminId, Guid userId, UpdateUserAppRoleRequest request)
    {
        if (adminId == userId)
            throw new InvalidOperationException("Admins cannot change their own app role.");

        await _userAppRoleRepo.ChangeRoleAsync(userId, request.AppId, request.RoleId);

        await _audit.LogAsync(adminId, request.AppId, "admin.user.app_role_updated", "user_app_roles",
            new { targetUserId = userId, newRoleId = request.RoleId });
    }

    private async Task<AdminUserDto> BuildAdminUserDtoAsync(User user)
    {
        var systemRoles = await _userRepo.GetSystemRoleEntitiesAsync(user.Id);
        var memberships = await _userRepo.GetProjectMembershipsAsync(user.Id);

        return new AdminUserDto
        {
            Id = user.Id,
            GitHubId = user.GitHubId,
            GitHubUsername = user.GitHubUsername,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            AvatarUrl = user.AvatarUrl,
            IsActive = user.IsActive,
            SystemRoles = systemRoles.Select(r => new AdminRoleSummaryDto { Id = r.Id, Name = r.Name }),
            ProjectMemberships = memberships,
            CreatedAt = user.CreatedAt
        };
    }

    private static void ValidateCreateRequest(CreateAdminUserRequest request)
    {
        if (request.GitHubId <= 0)
            throw new ValidationException("GitHub ID must be a positive integer.");

        if (string.IsNullOrWhiteSpace(request.GitHubUsername))
            throw new ValidationException("GitHub username is required.");

        if (request.FirstName is not null && (request.FirstName.Length < 2 || request.FirstName.Length > 100))
            throw new ValidationException("First name must be between 2 and 100 characters.");

        if (request.LastName is not null && (request.LastName.Length < 2 || request.LastName.Length > 100))
            throw new ValidationException("Last name must be between 2 and 100 characters.");

        if (request.Email is not null)
            ValidateEmail(request.Email);
    }

    private static void ValidateEmail(string email)
    {
        if (!new EmailAddressAttribute().IsValid(email))
            throw new ValidationException($"'{email}' is not a valid email address.");
    }
}
