namespace FlatPlanet.Platform.Domain.Entities;

public sealed class User
{
    public Guid Id { get; init; }
    public Guid? CompanyId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? RoleTitle { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }

    // Legacy fields — kept for backward compat with GitHub OAuth flow
    public long GitHubId { get; set; }
    public string GitHubUsername { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? GitHubAccessToken { get; set; } // AES-256 encrypted at rest
    public bool IsActive { get; set; } = true;
    public Guid? OnboardedBy { get; set; }
}
