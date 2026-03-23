namespace FlatPlanet.Platform.Domain.Entities;

public sealed class RefreshToken
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid? SessionId { get; init; }
    public string TokenHash { get; init; } = string.Empty; // SHA-256 of raw token
    public DateTime ExpiresAt { get; init; }
    public bool Revoked { get; set; }
    public DateTime CreatedAt { get; init; }
}
