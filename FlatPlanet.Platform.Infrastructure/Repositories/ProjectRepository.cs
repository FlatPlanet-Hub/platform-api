using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly IDbConnectionFactory _db;

    public ProjectRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Project?> GetByIdAsync(Guid id)
    {
        await using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Project>(
            "SELECT * FROM platform.projects WHERE id = @id", new { id });
    }

    public async Task<Project?> GetByAppIdAsync(Guid appId)
    {
        await using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Project>(
            "SELECT * FROM platform.projects WHERE app_id = @appId", new { appId });
    }

    public async Task<IEnumerable<Project>> GetByAppIdsAsync(IEnumerable<Guid> appIds)
    {
        await using var conn = _db.CreateConnection();
        const string sql = """
            SELECT * FROM platform.projects
            WHERE app_id = ANY(@appIds) AND is_active = true
            ORDER BY created_at DESC
            """;
        return await conn.QueryAsync<Project>(sql, new { appIds = appIds.ToArray() });
    }

    public async Task<Project> CreateAsync(Project project)
    {
        await using var conn = _db.CreateConnection();
        const string sql = """
            INSERT INTO platform.projects (id, name, description, schema_name, owner_id, app_id, app_slug, github_repo, tech_stack, is_active, created_at, updated_at)
            VALUES (@Id, @Name, @Description, @SchemaName, @OwnerId, @AppId, @AppSlug, @GitHubRepo, @TechStack, @IsActive, @CreatedAt, @UpdatedAt)
            RETURNING *
            """;
        return await conn.QuerySingleAsync<Project>(sql, project);
    }

    public async Task UpdateAsync(Project project)
    {
        await using var conn = _db.CreateConnection();
        const string sql = """
            UPDATE platform.projects
            SET name = @Name, description = @Description, github_repo = @GitHubRepo,
                tech_stack = @TechStack, is_active = @IsActive, app_id = @AppId,
                app_slug = @AppSlug, updated_at = @UpdatedAt
            WHERE id = @Id
            """;
        await conn.ExecuteAsync(sql, project);
    }
}
