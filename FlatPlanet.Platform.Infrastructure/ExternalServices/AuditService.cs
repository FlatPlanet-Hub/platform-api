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
            VALUES (gen_random_uuid(), @user_id::uuid, @app_id::uuid, @event_type, @ip_address, @details::jsonb, now())
            """, new { user_id = userId, app_id = appId, event_type = eventType, ip_address = ipAddress, details = detailsJson });
    }

    public async Task<object> QueryAsync(Guid? userId, Guid? appId, string? eventType, DateTime? from, DateTime? to, int page, int pageSize)
    {
        using var conn = db.CreateConnection();

        var conditions = new List<string>();
        if (userId.HasValue) conditions.Add("user_id = @user_id::uuid");
        if (appId.HasValue) conditions.Add("app_id = @app_id::uuid");
        if (!string.IsNullOrEmpty(eventType)) conditions.Add("event_type = @event_type");
        if (from.HasValue) conditions.Add("created_at >= @from");
        if (to.HasValue) conditions.Add("created_at <= @to");

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        var offset = (page - 1) * pageSize;

        var rows = await conn.QueryAsync($"""
            SELECT id, user_id, app_id, event_type, ip_address, details, created_at
            FROM platform.auth_audit_log
            {where}
            ORDER BY created_at DESC
            LIMIT @page_size OFFSET @offset
            """, new { user_id = userId, app_id = appId, event_type = eventType, from, to, page_size = pageSize, offset });

        return rows;
    }
}
