using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class ResourceRepository(IDbConnectionFactory connectionFactory) : IResourceRepository
{
    public async Task<Resource> CreateAsync(Resource resource)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync("""
            INSERT INTO platform.resources
                (id, app_id, resource_type_id, name, identifier, parent_id, status, created_at)
            VALUES
                (@Id, @AppId, @ResourceTypeId, @Name, @Identifier, @ParentId, @Status, @CreatedAt)
            """, resource);
        return resource;
    }

    public async Task<Resource?> GetByIdAsync(Guid id)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Resource>(
            "SELECT * FROM platform.resources WHERE id = @Id", new { Id = id });
    }

    public async Task<Resource?> GetByIdentifierAsync(Guid appId, string identifier)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Resource>(
            "SELECT * FROM platform.resources WHERE app_id = @AppId AND identifier = @Identifier",
            new { AppId = appId, Identifier = identifier });
    }

    public async Task<IEnumerable<Resource>> GetByAppIdAsync(Guid appId)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<Resource>(
            "SELECT * FROM platform.resources WHERE app_id = @AppId ORDER BY name",
            new { AppId = appId });
    }

    public async Task<IEnumerable<ResourceType>> GetAllTypesAsync()
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<ResourceType>(
            "SELECT * FROM platform.resource_types ORDER BY name");
    }

    public async Task UpdateAsync(Resource resource)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync("""
            UPDATE platform.resources
            SET name = @Name, identifier = @Identifier, status = @Status
            WHERE id = @Id
            """, resource);
    }

    public async Task DeactivateAsync(Guid id)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.resources SET status = 'inactive' WHERE id = @Id",
            new { Id = id });
    }
}
