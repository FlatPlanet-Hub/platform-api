namespace FlatPlanet.Platform.Domain.Entities;

public sealed class Session
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid? AppId { get; init; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime StartedAt { get; init; }
    public DateTime LastActiveAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? EndedReason { get; set; }
}
