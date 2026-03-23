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

    public async Task<IEnumerable<Project>> GetByUserIdAsync(Guid userId)
    {
        await using var conn = _db.CreateConnection();
        const string sql = """
            SELECT p.* FROM platform.projects p
            JOIN platform.project_members pm ON pm.project_id = p.id
            WHERE pm.user_id = @userId AND p.is_active = true AND pm.status = 'active'
            ORDER BY p.created_at DESC
            """;
        return await conn.QueryAsync<Project>(sql, new { userId });
    }

    public async Task<Project> CreateAsync(Project project)
    {
        await using var conn = _db.CreateConnection();
        const string sql = """
            INSERT INTO platform.projects (id, name, description, schema_name, owner_id, github_repo, is_active, created_at, updated_at)
            VALUES (@Id, @Name, @Description, @SchemaName, @OwnerId, @GitHubRepo, @IsActive, @CreatedAt, @UpdatedAt)
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
                is_active = @IsActive, updated_at = @UpdatedAt
            WHERE id = @Id
            """;
        await conn.ExecuteAsync(sql, project);
    }
}
