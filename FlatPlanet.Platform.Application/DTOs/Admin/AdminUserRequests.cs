namespace FlatPlanet.Platform.Application.DTOs.Admin;

public sealed class CreateAdminUserRequest
{
    public long GitHubId { get; init; }
    public string GitHubUsername { get; init; } = string.Empty;
    public string? AvatarUrl { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public IEnumerable<Guid> RoleIds { get; init; } = [];
    public IEnumerable<ProjectAssignmentRequest> ProjectAssignments { get; init; } = [];
}

public sealed class ProjectAssignmentRequest
{
    public Guid ProjectId { get; init; }
    public Guid ProjectRoleId { get; init; }
}

public sealed class BulkCreateUsersRequest
{
    public IEnumerable<CreateAdminUserRequest> Users { get; init; } = [];
}

public sealed class UpdateAdminUserRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public bool? IsActive { get; init; }
}

public sealed class UpdateUserRolesRequest
{
    public IEnumerable<Guid> RoleIds { get; init; } = [];
}

public sealed class UpdateUserProjectRoleRequest
{
    public Guid ProjectRoleId { get; init; }
}

public sealed class UpdateUserStatusRequest
{
    /// <summary>Accepted values: "active", "inactive", "suspended"</summary>
    public string Status { get; init; } = string.Empty;
}

public sealed class UpdateUserAppRoleRequest
{
    public Guid AppId { get; init; }
    public Guid RoleId { get; init; }
}

public sealed class AdminUserListFilter
{
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
    public Guid? RoleId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
