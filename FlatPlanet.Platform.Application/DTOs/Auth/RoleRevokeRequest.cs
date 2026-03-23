namespace FlatPlanet.Platform.Application.DTOs.Auth;

public sealed class RoleRevokeRequest
{
    public Guid UserId { get; init; }
    public string RoleName { get; init; } = string.Empty;
}
