namespace SupabaseProxy.Application.DTOs.Auth;

public sealed class UserProfileResponse
{
    public Guid Id { get; init; }
    public string GitHubUsername { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
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
