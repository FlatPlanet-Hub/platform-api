using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Infrastructure.Configuration;

namespace SupabaseProxy.Infrastructure.ExternalServices;

public sealed class AuditService : IAuditService
{
    private readonly SupabaseSettings _settings;

    public AuditService(IOptions<SupabaseSettings> settings) => _settings = settings.Value;

    public async Task LogAsync(Guid? userId, Guid? projectId, string action, string? resource = null, object? details = null, string? ipAddress = null)
    {
        await using var conn = new NpgsqlConnection(_settings.BuildConnectionString());
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
