namespace FlatPlanet.Platform.Application.DTOs.Auth;

public sealed class UserProfileResponse
{
    public Guid Id { get; init; }
    public string GitHubUsername { get; init; } = string.Empty;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? AvatarUrl { get; init; }
    public IEnumerable<string> SystemRoles { get; init; } = [];
    public IEnumerable<UserProjectSummaryDto> Projects { get; init; } = [];
}

public sealed class UserProjectSummaryDto
{
    public Guid ProjectId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Schema { get; init; } = string.Empty;
    public string ProjectRole { get; init; } = string.Empty;
    public string[] Permissions { get; init; } = [];
}
