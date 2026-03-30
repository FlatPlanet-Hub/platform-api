using System.Text.Json;
using Dapper;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<AuditLogRepository> _logger;

    public AuditLogRepository(IDbConnectionFactory db, ILogger<AuditLogRepository> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task LogAsync(Guid actorId, string actorEmail, string action,
        string targetType, Guid? targetId, object? details, string? ipAddress)
    {
        try
        {
            await using var conn = _db.CreateConnection();
            var detailsJson = details is not null ? JsonSerializer.Serialize(details) : null;

            await conn.ExecuteAsync("""
                INSERT INTO platform.audit_log (id, actor_id, actor_email, action, target_type, target_id, details, ip_address, created_at)
                VALUES (gen_random_uuid(), @actorId, @actorEmail, @action, @targetType, @targetId, @details::jsonb, @ipAddress, now())
                """, new { actorId, actorEmail, action, targetType, targetId, details = detailsJson, ipAddress });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AUDIT FAILURE: {Action} on {TargetType} {TargetId}", action, targetType, targetId);
        }
    }

    public async Task<IEnumerable<AuditLogDto>> GetPagedAsync(int page, int pageSize,
        Guid? actorId, DateTime? from, DateTime? to)
    {
        await using var conn = _db.CreateConnection();

        var conditions = new List<string>();
        if (actorId.HasValue) conditions.Add("actor_id = @actorId");
        if (from.HasValue)    conditions.Add("created_at >= @from");
        if (to.HasValue)      conditions.Add("created_at <= @to");

        var where  = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        var offset = (page - 1) * pageSize;

        return await conn.QueryAsync<AuditLogDto>($"""
            SELECT id, actor_email, action, target_type, target_id, created_at
            FROM platform.audit_log
            {where}
            ORDER BY created_at DESC
            LIMIT @pageSize OFFSET @offset
            """, new { actorId, from, to, pageSize, offset });
    }

    public async Task DeleteExpiredAsync(int retentionDays)
    {
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("""
            DELETE FROM platform.audit_log
            WHERE created_at < now() - (@retentionDays || ' days')::INTERVAL
            """, new { retentionDays });
    }
}
