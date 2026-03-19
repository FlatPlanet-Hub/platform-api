namespace SupabaseProxy.Application.DTOs.Project;

public sealed class InviteUserRequest
{
    public string GitHubUsername { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
}
