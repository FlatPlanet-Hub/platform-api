using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;
using SupabaseProxy.Infrastructure.Configuration;

namespace SupabaseProxy.Infrastructure.Repositories;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly SupabaseSettings _settings;

    public ProjectRepository(IOptions<SupabaseSettings> settings) => _settings = settings.Value;

    private NpgsqlConnection CreateConnection() => new(_settings.BuildConnectionString());

    public async Task<Project?> GetByIdAsync(Guid id)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Project>(
            "SELECT * FROM platform.projects WHERE id = @id", new { id });
    }

    public async Task<IEnumerable<Project>> GetByUserIdAsync(Guid userId)
    {
        await using var conn = CreateConnection();
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
        await using var conn = CreateConnection();
        const string sql = """
            INSERT INTO platform.projects (id, name, description, schema_name, owner_id, github_repo, is_active, created_at, updated_at)
            VALUES (@Id, @Name, @Description, @SchemaName, @OwnerId, @GitHubRepo, @IsActive, @CreatedAt, @UpdatedAt)
            RETURNING *
            """;
        return await conn.QuerySingleAsync<Project>(sql, project);
    }

    public async Task UpdateAsync(Project project)
    {
        await using var conn = CreateConnection();
        const string sql = """
            UPDATE platform.projects
            SET name = @Name, description = @Description, github_repo = @GitHubRepo,
                is_active = @IsActive, updated_at = @UpdatedAt
            WHERE id = @Id
            """;
        await conn.ExecuteAsync(sql, project);
    }

    // ── Project Roles ────────────────────────────────────────────────────────

    public async Task<ProjectRole> CreateRoleAsync(ProjectRole role)
    {
        await using var conn = CreateConnection();
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

    public async Task<ProjectRole?> GetRoleAsync(Guid projectId, Guid roleId)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ProjectRole>(
            "SELECT * FROM platform.project_roles WHERE project_id = @projectId AND id = @roleId",
            new { projectId, roleId });
    }

    public async Task<ProjectRole?> GetRoleByNameAsync(Guid projectId, string name)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ProjectRole>(
            "SELECT * FROM platform.project_roles WHERE project_id = @projectId AND name = @name",
            new { projectId, name });
    }

    public async Task<IEnumerable<ProjectRole>> GetRolesAsync(Guid projectId)
    {
        await using var conn = CreateConnection();
        return await conn.QueryAsync<ProjectRole>(
            "SELECT * FROM platform.project_roles WHERE project_id = @projectId ORDER BY name",
            new { projectId });
    }

    public async Task UpdateRoleAsync(ProjectRole role)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.project_roles SET permissions = @Permissions WHERE id = @Id",
            new { Permissions = role.Permissions, role.Id });
    }

    public async Task DeleteRoleAsync(Guid roleId)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM platform.project_roles WHERE id = @roleId AND is_default = false",
            new { roleId });
    }

    // ── Project Members ──────────────────────────────────────────────────────

    public async Task<ProjectMember?> GetMemberAsync(Guid projectId, Guid userId)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ProjectMember>(
            "SELECT * FROM platform.project_members WHERE project_id = @projectId AND user_id = @userId",
            new { projectId, userId });
    }

    public async Task<IEnumerable<ProjectMember>> GetMembersAsync(Guid projectId)
    {
        await using var conn = CreateConnection();
        return await conn.QueryAsync<ProjectMember>(
            "SELECT * FROM platform.project_members WHERE project_id = @projectId AND status = 'active'",
            new { projectId });
    }

    public async Task AddMemberAsync(ProjectMember member)
    {
        await using var conn = CreateConnection();
        const string sql = """
            INSERT INTO platform.project_members (id, project_id, user_id, project_role_id, invited_by, status, joined_at)
            VALUES (@Id, @ProjectId, @UserId, @ProjectRoleId, @InvitedBy, @Status, @JoinedAt)
            """;
        await conn.ExecuteAsync(sql, member);
    }

    public async Task UpdateMemberAsync(ProjectMember member)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.project_members SET project_role_id = @ProjectRoleId WHERE project_id = @ProjectId AND user_id = @UserId",
            new { member.ProjectRoleId, member.ProjectId, member.UserId });
    }

    public async Task RemoveMemberAsync(Guid projectId, Guid userId)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM platform.project_members WHERE project_id = @projectId AND user_id = @userId",
            new { projectId, userId });
    }
}
