using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class ProjectMemberRepository : IProjectMemberRepository
{
    private readonly IDbConnectionFactory _db;

    public ProjectMemberRepository(IDbConnectionFactory db) => _db = db;

    public async Task<ProjectMember?> GetAsync(Guid projectId, Guid userId)
    {
        await using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ProjectMember>(
            "SELECT * FROM platform.project_members WHERE project_id = @projectId AND user_id = @userId",
            new { projectId, userId });
    }

    public async Task<IEnumerable<ProjectMember>> GetByProjectIdAsync(Guid projectId)
    {
        await using var conn = _db.CreateConnection();
        return await conn.QueryAsync<ProjectMember>(
            "SELECT * FROM platform.project_members WHERE project_id = @projectId AND status = 'active'",
            new { projectId });
    }

    public async Task AddAsync(ProjectMember member)
    {
        await using var conn = _db.CreateConnection();
        const string sql = """
            INSERT INTO platform.project_members (id, project_id, user_id, project_role_id, invited_by, status, joined_at)
            VALUES (@Id, @ProjectId, @UserId, @ProjectRoleId, @InvitedBy, @Status, @JoinedAt)
            """;
        await conn.ExecuteAsync(sql, member);
    }

    public async Task UpdateAsync(ProjectMember member)
    {
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.project_members SET project_role_id = @ProjectRoleId WHERE project_id = @ProjectId AND user_id = @UserId",
            new { member.ProjectRoleId, member.ProjectId, member.UserId });
    }

    public async Task RemoveAsync(Guid projectId, Guid userId)
    {
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM platform.project_members WHERE project_id = @projectId AND user_id = @userId",
            new { projectId, userId });
    }
}
