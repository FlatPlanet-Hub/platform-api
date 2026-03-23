namespace FlatPlanet.Platform.Domain.Entities;

public sealed class AttendanceEvent
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string EventType { get; init; } = string.Empty; // 'clock_in', 'clock_out', 'break_start', 'break_end'
    public DateTime Timestamp { get; init; }
    public DateOnly DateSydney { get; init; }
    public string? IpAddress { get; init; }
    public string? Notes { get; set; }
}
