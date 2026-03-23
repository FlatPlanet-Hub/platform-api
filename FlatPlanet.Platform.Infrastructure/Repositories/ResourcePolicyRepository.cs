using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class ResourcePolicyRepository(IDbConnectionFactory db) : IResourcePolicyRepository
{
    public async Task<IEnumerable<ResourcePolicy>> GetByResourceIdAsync(Guid resourceId)
    {
        await using var conn = db.CreateConnection();
        return await conn.QueryAsync<ResourcePolicy>(
            "SELECT * FROM platform.resource_policies WHERE resource_id = @resourceId",
            new { resourceId });
    }
}
