namespace SupabaseProxy.Application.DTOs.Auth;

public sealed class GitHubUserProfile
{
    public long Id { get; init; }
    public string Login { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? Email { get; init; }
    public string? AvatarUrl { get; init; }
    public string AccessToken { get; init; } = string.Empty;
}
