namespace FlatPlanet.Platform.Application.DTOs.Project;

public sealed class InviteUserRequest
{
    public Guid UserId { get; init; }
    public string Role { get; init; } = string.Empty;
}
