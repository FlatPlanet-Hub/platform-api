using System.Text.Json;
using Dapper;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class AuditService(IDbConnectionFactory db) : IAuditService
{
    public async Task LogAsync(Guid? userId, Guid? appId, string eventType, string? resource = null, object? details = null, string? ipAddress = null)
    {
        using var conn = db.CreateConnection();
        var detailsJson = details is not null ? JsonSerializer.Serialize(details) : null;

        // Write to auth_audit_log (Feature 6)
        await conn.ExecuteAsync("""
            INSERT INTO platform.auth_audit_log (id, user_id, app_id, event_type, ip_address, details, created_at)
            VALUES (gen_random_uuid(), @userId, @appId, @eventType, @ipAddress, @details::jsonb, now())
            """, new { userId, appId, eventType, ipAddress, details = detailsJson });
    }

    public async Task<object> QueryAsync(Guid? userId, Guid? appId, string? eventType, DateTime? from, DateTime? to, int page, int pageSize)
    {
        using var conn = db.CreateConnection();

        var conditions = new List<string>();
        if (userId.HasValue) conditions.Add("user_id = @UserId");
        if (appId.HasValue) conditions.Add("app_id = @AppId");
        if (!string.IsNullOrEmpty(eventType)) conditions.Add("event_type = @EventType");
        if (from.HasValue) conditions.Add("created_at >= @From");
        if (to.HasValue) conditions.Add("created_at <= @To");

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        var offset = (page - 1) * pageSize;

        var rows = await conn.QueryAsync($"""
            SELECT id, user_id, app_id, event_type, ip_address, details, created_at
            FROM platform.auth_audit_log
            {where}
            ORDER BY created_at DESC
            LIMIT @PageSize OFFSET @Offset
            """, new { userId, appId, eventType, from, to, PageSize = pageSize, Offset = offset });

        return rows;
    }
}
