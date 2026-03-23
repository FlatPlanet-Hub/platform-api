using System.Text.Json;
using Dapper;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class AuditService : IAuditService
{
    private readonly IDbConnectionFactory _db;

    public AuditService(IDbConnectionFactory db) => _db = db;

    public async Task LogAsync(Guid? userId, Guid? projectId, string action, string? resource = null, object? details = null, string? ipAddress = null)
    {
        await using var conn = _db.CreateConnection();
        const string sql = """
            INSERT INTO platform.audit_log (id, user_id, project_id, action, resource, details, ip_address, created_at)
            VALUES (gen_random_uuid(), @userId, @projectId, @action, @resource, @details::jsonb, @ipAddress, now())
            """;
        await conn.ExecuteAsync(sql, new
        {
            userId,
            projectId,
            action,
            resource,
            details = details is not null ? JsonSerializer.Serialize(details) : null,
            ipAddress
        });
    }
}
