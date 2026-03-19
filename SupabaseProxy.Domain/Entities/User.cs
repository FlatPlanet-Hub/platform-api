namespace SupabaseProxy.Domain.Entities;

public sealed class User
{
    public Guid Id { get; init; }
    public long GitHubId { get; set; }
    public string GitHubUsername { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? GitHubAccessToken { get; set; } // AES-256 encrypted at rest
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}
