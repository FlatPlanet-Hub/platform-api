namespace FlatPlanet.Platform.Application.DTOs.Security;

public sealed class SecurityPlatformUserDto
{
    public Guid Id { get; init; }
    public string GitHubUsername { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
}
