namespace FlatPlanet.Platform.Domain.Entities;

/// <summary>
/// Long-lived API token for service-to-service auth (Claude Code, CI/CD, integrations).
/// Replaces the old ClaudeToken entity.
/// </summary>
public sealed class ApiToken
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid? AppId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string TokenHash { get; init; } = string.Empty;
    public string[] Permissions { get; set; } = [];
    public DateTime ExpiresAt { get; init; }
    public bool Revoked { get; set; }
    public string? RevokedReason { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; init; }
}
