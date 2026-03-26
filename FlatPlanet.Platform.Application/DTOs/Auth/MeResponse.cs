namespace FlatPlanet.Platform.Application.DTOs.Auth;

public sealed class MeResponse
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public Guid? CompanyId { get; init; }
    public bool CanCreateProject { get; init; }
}
