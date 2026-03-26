namespace FlatPlanet.Platform.Application.DTOs.SecurityPlatform;

public sealed class SpUserDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? RoleTitle { get; init; }
    public string Status { get; init; } = string.Empty;
    public List<SpAppAccessDto> AppAccess { get; init; } = [];
}

public sealed class SpAppAccessDto
{
    public Guid AppId { get; init; }
    public string AppSlug { get; init; } = string.Empty;
    public string AppName { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public string[] Permissions { get; init; } = [];
    public DateTime GrantedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
}
