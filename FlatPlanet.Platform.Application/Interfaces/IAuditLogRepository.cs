using FlatPlanet.Platform.Application.DTOs;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IAuditLogRepository
{
    Task LogAsync(Guid actorId, string actorEmail, string action,
                  string targetType, Guid? targetId, object? details, string? ipAddress);
    Task<IEnumerable<AuditLogDto>> GetPagedAsync(int page, int pageSize,
                  Guid? actorId, DateTime? from, DateTime? to);
    Task DeleteExpiredAsync(int retentionDays);
}
