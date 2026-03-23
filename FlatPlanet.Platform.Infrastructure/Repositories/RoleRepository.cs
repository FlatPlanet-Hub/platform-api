using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class RoleRepository : IRoleRepository
{
    private readonly IDbConnectionFactory _db;

    public RoleRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<Role>> GetAllAsync()
    {
        await using var conn = _db.CreateConnection();
        return await conn.QueryAsync<Role>("SELECT * FROM platform.roles ORDER BY name");
    }

    public async Task<Role?> GetByIdAsync(Guid id)
    {
        await using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Role>(
            "SELECT * FROM platform.roles WHERE id = @id", new { id });
    }

    public async Task<Role?> GetByNameAsync(string name)
    {
        await using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Role>(
            "SELECT * FROM platform.roles WHERE name = @name", new { name });
    }
}
