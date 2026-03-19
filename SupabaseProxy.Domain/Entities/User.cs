namespace SupabaseProxy.Domain.Entities;

public sealed class User
{
    public Guid Id { get; init; }
    public long GitHubId { get; set; }
    public string GitHubUsername { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? AvatarUrl { get; set; }
    public string? GitHubAccessToken { get; set; } // AES-256 encrypted at rest
    public bool IsActive { get; set; } = true;
    public Guid? OnboardedBy { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}
