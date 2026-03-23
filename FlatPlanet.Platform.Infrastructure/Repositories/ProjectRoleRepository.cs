using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class ProjectRoleRepository : IProjectRoleRepository
{
    private readonly IDbConnectionFactory _db;

    public ProjectRoleRepository(IDbConnectionFactory db) => _db = db;

    public async Task<ProjectRole> CreateAsync(ProjectRole role)
    {
        await using var conn = _db.CreateConnection();
        const string sql = """
            INSERT INTO platform.project_roles (id, project_id, name, permissions, is_default, created_at)
            VALUES (@Id, @ProjectId, @Name, @Permissions, @IsDefault, @CreatedAt)
            RETURNING *
            """;
        return await conn.QuerySingleAsync<ProjectRole>(sql, new
        {
            role.Id, role.ProjectId, role.Name,
            Permissions = role.Permissions,
            role.IsDefault, role.CreatedAt
        });
    }

    public async Task<ProjectRole?> GetByIdAsync(Guid projectId, Guid roleId)
    {
        await using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ProjectRole>(
            "SELECT * FROM platform.project_roles WHERE project_id = @projectId AND id = @roleId",
            new { projectId, roleId });
    }

    public async Task<ProjectRole?> GetByNameAsync(Guid projectId, string name)
    {
        await using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ProjectRole>(
            "SELECT * FROM platform.project_roles WHERE project_id = @projectId AND name = @name",
            new { projectId, name });
    }

    public async Task<IEnumerable<ProjectRole>> GetByProjectIdAsync(Guid projectId)
    {
        await using var conn = _db.CreateConnection();
        return await conn.QueryAsync<ProjectRole>(
            "SELECT * FROM platform.project_roles WHERE project_id = @projectId ORDER BY name",
            new { projectId });
    }

    public async Task UpdateAsync(ProjectRole role)
    {
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.project_roles SET permissions = @Permissions WHERE id = @Id",
            new { Permissions = role.Permissions, role.Id });
    }

    public async Task DeleteAsync(Guid roleId)
    {
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM platform.project_roles WHERE id = @roleId AND is_default = false",
            new { roleId });
    }
}
