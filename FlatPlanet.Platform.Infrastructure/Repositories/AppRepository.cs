using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class AppRepository(IDbConnectionFactory connectionFactory) : IAppRepository
{
    public async Task<App> CreateAsync(App app)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync("""
            INSERT INTO platform.apps
                (id, company_id, name, slug, base_url, schema_name, status, registered_by, created_at, updated_at)
            VALUES
                (@Id, @CompanyId, @Name, @Slug, @BaseUrl, @SchemaName, @Status, @RegisteredBy, @CreatedAt, @UpdatedAt)
            """, app);
        return app;
    }

    public async Task<App?> GetByIdAsync(Guid id)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<App>(
            "SELECT * FROM platform.apps WHERE id = @Id", new { Id = id });
    }

    public async Task<App?> GetBySlugAsync(string slug)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<App>(
            "SELECT * FROM platform.apps WHERE slug = @Slug", new { Slug = slug });
    }

    public async Task<IEnumerable<App>> GetAllAsync()
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<App>(
            "SELECT * FROM platform.apps ORDER BY name");
    }

    public async Task<IEnumerable<App>> GetByCompanyIdAsync(Guid companyId)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<App>(
            "SELECT * FROM platform.apps WHERE company_id = @CompanyId ORDER BY name",
            new { CompanyId = companyId });
    }

    public async Task<IEnumerable<App>> GetByUserIdAsync(Guid userId)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<App>("""
            SELECT DISTINCT a.* FROM platform.apps a
            INNER JOIN platform.user_app_roles uar ON uar.app_id = a.id
            WHERE uar.user_id = @UserId AND uar.status = 'active'
            ORDER BY a.name
            """, new { UserId = userId });
    }

    public async Task UpdateAsync(App app)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync("""
            UPDATE platform.apps
            SET name = @Name, base_url = @BaseUrl, updated_at = now()
            WHERE id = @Id
            """, app);
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.apps SET status = @Status, updated_at = now() WHERE id = @Id",
            new { Id = id, Status = status });
    }
}
