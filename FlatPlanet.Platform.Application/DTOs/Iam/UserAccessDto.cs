namespace FlatPlanet.Platform.Application.DTOs.Iam;

public sealed class GrantUserAccessRequest
{
    public Guid UserId { get; init; }
    public Guid RoleId { get; init; }
    public DateTime? ExpiresAt { get; init; }
}

public sealed class ChangeUserRoleRequest
{
    public Guid RoleId { get; init; }
}

public sealed class AppUserDto
{
    public Guid UserId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public Guid RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public DateTime GrantedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string Status { get; init; } = string.Empty;
}
