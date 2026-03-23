using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class PermissionRepository : IPermissionRepository
{
    private readonly IDbConnectionFactory _db;

    public PermissionRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<Permission>> GetAllAsync()
    {
        await using var conn = _db.CreateConnection();
        return await conn.QueryAsync<Permission>(
            "SELECT * FROM platform.permissions ORDER BY category, name");
    }
}
