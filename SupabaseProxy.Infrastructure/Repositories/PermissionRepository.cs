using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;
using SupabaseProxy.Infrastructure.Configuration;

namespace SupabaseProxy.Infrastructure.Repositories;

public sealed class PermissionRepository : IPermissionRepository
{
    private readonly SupabaseSettings _settings;

    public PermissionRepository(IOptions<SupabaseSettings> settings) => _settings = settings.Value;

    private NpgsqlConnection CreateConnection() => new(_settings.BuildConnectionString());

    public async Task<IEnumerable<Permission>> GetAllAsync()
    {
        await using var conn = CreateConnection();
        return await conn.QueryAsync<Permission>(
            "SELECT * FROM platform.permissions ORDER BY category, name");
    }
}
