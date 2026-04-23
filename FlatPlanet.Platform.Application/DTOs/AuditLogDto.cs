namespace FlatPlanet.Platform.Application.DTOs;

public class AuditLogDto
{
    public Guid     Id         { get; set; }
    public string   ActorEmail { get; set; } = string.Empty;
    public string   Action     { get; set; } = string.Empty;
    public string   TargetType { get; set; } = string.Empty;
    public Guid?    TargetId   { get; set; }
    public DateTime CreatedAt  { get; set; }
}
