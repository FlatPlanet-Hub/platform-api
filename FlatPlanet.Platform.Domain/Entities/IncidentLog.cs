namespace FlatPlanet.Platform.Domain.Entities;

public sealed class IncidentLog
{
    public Guid Id { get; init; }
    public Guid? ReportedBy { get; init; }
    public string Severity { get; set; } = string.Empty;    // 'low', 'medium', 'high', 'critical'
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid? AffectedAppId { get; set; }
    public int? AffectedUsersCount { get; set; }
    public string Status { get; set; } = "open";            // 'open', 'investigating', 'resolved', 'closed'
    public string? Resolution { get; set; }
    public DateTime ReportedAt { get; init; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}
