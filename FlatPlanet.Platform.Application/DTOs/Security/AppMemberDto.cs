namespace FlatPlanet.Platform.Application.DTOs.Security;

public sealed class AppMemberDto
{
    public Guid UserId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public string[] Permissions { get; init; } = [];
    public DateTime GrantedAt { get; init; }
}
