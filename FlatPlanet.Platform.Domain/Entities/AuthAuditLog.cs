namespace FlatPlanet.Platform.Domain.Entities;

public sealed class AuthAuditLog
{
    public Guid Id { get; init; }
    public Guid? UserId { get; init; }
    public Guid? AppId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? Details { get; init; } // JSON
    public DateTime CreatedAt { get; init; }
}
