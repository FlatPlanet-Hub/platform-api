using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;
using SupabaseProxy.Infrastructure.Configuration;

namespace SupabaseProxy.Infrastructure.Repositories;

public sealed class RoleRepository : IRoleRepository
{
    private readonly SupabaseSettings _settings;

    public RoleRepository(IOptions<SupabaseSettings> settings) => _settings = settings.Value;

    private NpgsqlConnection CreateConnection() => new(_settings.BuildConnectionString());

    public async Task<IEnumerable<Role>> GetAllAsync()
    {
        await using var conn = CreateConnection();
        return await conn.QueryAsync<Role>("SELECT * FROM platform.roles ORDER BY name");
    }

    public async Task<Role?> GetByNameAsync(string name)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Role>(
            "SELECT * FROM platform.roles WHERE name = @name", new { name });
    }
}
