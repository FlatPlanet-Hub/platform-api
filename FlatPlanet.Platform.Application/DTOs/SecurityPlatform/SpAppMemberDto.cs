namespace FlatPlanet.Platform.Application.DTOs.SecurityPlatform;

public sealed class SpAppMemberDto
{
    public Guid UserId { get; init; }
    public Guid RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public string[] Permissions { get; init; } = [];
    public string Status { get; init; } = string.Empty;
    public DateTime GrantedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
}
