namespace FlatPlanet.Platform.Application.DTOs.Security;

public sealed class UserAppRoleDto
{
    public Guid AppId { get; init; }
    public string AppSlug { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public string[] Permissions { get; init; } = [];
}
