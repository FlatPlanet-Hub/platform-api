using System.Text.Json;
using Dapper;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class AuditLogRepository(IDbConnectionFactory db, ILogger<AuditLogRepository> logger) : IAuditLogRepository
{
    public async Task LogAsync(Guid actorId, string actorEmail, string action,
                               string targetType, Guid? targetId, object? details, string? ipAddress)
    {
        try
        {
            var detailsJson = details is not null ? JsonSerializer.Serialize(details) : null;
            using var conn = db.CreateConnection();
            await conn.ExecuteAsync("""
                INSERT INTO platform.audit_log (actor_id, actor_email, action, target_type, target_id, details, ip_address)
                VALUES (@actor_id::uuid, @actor_email, @action, @target_type, @target_id::uuid, @details::jsonb, @ip_address)
                """,
                new { actor_id = actorId, actor_email = actorEmail, action, target_type = targetType,
                      target_id = targetId, details = detailsJson, ip_address = ipAddress });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AUDIT FAILURE: {Action} on {TargetType} {TargetId}", action, targetType, targetId);
        }
    }

    public async Task<IEnumerable<AuditLogDto>> GetPagedAsync(int page, int pageSize,
                                                               Guid? actorId, DateTime? from, DateTime? to)
    {
        var conditions = new List<string>();
        if (actorId.HasValue) conditions.Add("actor_id = @actor_id::uuid");
        if (from.HasValue)    conditions.Add("created_at >= @from");
        if (to.HasValue)      conditions.Add("created_at <= @to");

        var where  = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        var offset = (page - 1) * pageSize;

        using var conn = db.CreateConnection();
        return await conn.QueryAsync<AuditLogDto>($"""
            SELECT id, actor_email, action, target_type, target_id, created_at
            FROM platform.audit_log
            {where}
            ORDER BY created_at DESC
            LIMIT @page_size OFFSET @offset
            """,
            new { actor_id = actorId, from, to, page_size = pageSize, offset });
    }

    public async Task DeleteExpiredAsync(int retentionDays)
    {
        try
        {
            using var conn = db.CreateConnection();
            await conn.ExecuteAsync("""
                DELETE FROM platform.audit_log
                WHERE created_at < now() - (@retention_days || ' days')::INTERVAL
                """,
                new { retention_days = retentionDays });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AUDIT CLEANUP FAILURE: failed to delete expired audit log rows");
        }
    }
}
