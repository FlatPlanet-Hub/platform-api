namespace FlatPlanet.Platform.Domain.Entities;

public sealed class ClaudeToken
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid ProjectId { get; init; }
    public string TokenHash { get; init; } = string.Empty; // SHA-256 of raw JWT
    public DateTime ExpiresAt { get; init; }
    public bool Revoked { get; set; }
    public DateTime CreatedAt { get; init; }
}
