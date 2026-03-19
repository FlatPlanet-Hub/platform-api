using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SupabaseProxy.Application.DTOs.Admin;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;
using SupabaseProxy.Infrastructure.Configuration;

namespace SupabaseProxy.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly SupabaseSettings _settings;

    public UserRepository(IOptions<SupabaseSettings> settings) => _settings = settings.Value;

    private NpgsqlConnection CreateConnection() => new(_settings.BuildConnectionString());

    public async Task<User?> GetByIdAsync(Guid id)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<User>(
            "SELECT * FROM platform.users WHERE id = @id", new { id });
    }

    public async Task<User?> GetByGitHubIdAsync(long githubId)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<User>(
            "SELECT * FROM platform.users WHERE github_id = @githubId", new { githubId });
    }

    public async Task<User?> GetByGitHubUsernameAsync(string username)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<User>(
            "SELECT * FROM platform.users WHERE github_username = @username", new { username });
    }

    public async Task<User> CreateAsync(User user)
    {
        await using var conn = CreateConnection();
        const string sql = """
            INSERT INTO platform.users
                (id, github_id, github_username, first_name, last_name, email, avatar_url,
                 github_access_token, is_active, onboarded_by, created_at, updated_at)
            VALUES
                (@Id, @GitHubId, @GitHubUsername, @FirstName, @LastName, @Email, @AvatarUrl,
                 @GitHubAccessToken, @IsActive, @OnboardedBy, @CreatedAt, @UpdatedAt)
            RETURNING *
            """;
        return await conn.QuerySingleAsync<User>(sql, user);
    }

    public async Task UpdateAsync(User user)
    {
        await using var conn = CreateConnection();
        const string sql = """
            UPDATE platform.users
            SET github_username      = @GitHubUsername,
                first_name           = @FirstName,
                last_name            = @LastName,
                email                = @Email,
                avatar_url           = @AvatarUrl,
                github_access_token  = @GitHubAccessToken,
                is_active            = @IsActive,
                updated_at           = @UpdatedAt
            WHERE id = @Id
            """;
        await conn.ExecuteAsync(sql, user);
    }

    public async Task<IEnumerable<string>> GetSystemRolesAsync(Guid userId)
    {
        await using var conn = CreateConnection();
        const string sql = """
            SELECT r.name FROM platform.roles r
            JOIN platform.user_roles ur ON ur.role_id = r.id
            WHERE ur.user_id = @userId
            """;
        return await conn.QueryAsync<string>(sql, new { userId });
    }

    public async Task<IEnumerable<Role>> GetSystemRoleEntitiesAsync(Guid userId)
    {
        await using var conn = CreateConnection();
        const string sql = """
            SELECT r.id, r.name, r.description, r.is_system, r.created_at
            FROM platform.roles r
            JOIN platform.user_roles ur ON ur.role_id = r.id
            WHERE ur.user_id = @userId
            """;
        return await conn.QueryAsync<Role>(sql, new { userId });
    }

    public async Task AssignSystemRoleAsync(Guid userId, Guid roleId, Guid assignedBy)
    {
        await using var conn = CreateConnection();
        const string sql = """
            INSERT INTO platform.user_roles (id, user_id, role_id, assigned_by, assigned_at)
            VALUES (gen_random_uuid(), @userId, @roleId, @assignedBy, now())
            ON CONFLICT (user_id, role_id) DO NOTHING
            """;
        await conn.ExecuteAsync(sql, new { userId, roleId, assignedBy });
    }

    public async Task RevokeSystemRoleAsync(Guid userId, Guid roleId)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM platform.user_roles WHERE user_id = @userId AND role_id = @roleId",
            new { userId, roleId });
    }

    public async Task RevokeAllSystemRolesAsync(Guid userId)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM platform.user_roles WHERE user_id = @userId",
            new { userId });
    }

    public async Task SetSystemRolesAsync(Guid userId, IEnumerable<Guid> roleIds, Guid assignedBy)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync(
            "DELETE FROM platform.user_roles WHERE user_id = @userId",
            new { userId }, tx);

        foreach (var roleId in roleIds)
        {
            await conn.ExecuteAsync("""
                INSERT INTO platform.user_roles (id, user_id, role_id, assigned_by, assigned_at)
                VALUES (gen_random_uuid(), @userId, @roleId, @assignedBy, now())
                ON CONFLICT (user_id, role_id) DO NOTHING
                """, new { userId, roleId, assignedBy }, tx);
        }

        await tx.CommitAsync();
    }

    public async Task<(IEnumerable<User> Users, int TotalCount)> ListAsync(AdminUserListFilter filter)
    {
        await using var conn = CreateConnection();

        var where = new List<string>();
        var param = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            where.Add("(u.github_username ILIKE @search OR u.email ILIKE @search OR u.first_name ILIKE @search OR u.last_name ILIKE @search)");
            param.Add("search", $"%{filter.Search}%");
        }

        if (filter.IsActive.HasValue)
        {
            where.Add("u.is_active = @isActive");
            param.Add("isActive", filter.IsActive.Value);
        }

        if (filter.RoleId.HasValue)
        {
            where.Add("EXISTS (SELECT 1 FROM platform.user_roles ur WHERE ur.user_id = u.id AND ur.role_id = @roleId)");
            param.Add("roleId", filter.RoleId.Value);
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;
        var offset = (filter.Page - 1) * filter.PageSize;
        param.Add("limit", filter.PageSize);
        param.Add("offset", offset);

        var countSql = $"SELECT COUNT(*) FROM platform.users u {whereClause}";
        var dataSql = $"SELECT u.* FROM platform.users u {whereClause} ORDER BY u.created_at DESC LIMIT @limit OFFSET @offset";

        var total = await conn.ExecuteScalarAsync<int>(countSql, param);
        var users = await conn.QueryAsync<User>(dataSql, param);

        return (users, total);
    }

    public async Task<IEnumerable<AdminProjectMembershipDto>> GetProjectMembershipsAsync(Guid userId)
    {
        await using var conn = CreateConnection();
        const string sql = """
            SELECT p.id AS ProjectId, p.name AS ProjectName,
                   pr.name AS ProjectRole, pr.permissions AS Permissions
            FROM platform.project_members pm
            JOIN platform.projects p ON p.id = pm.project_id
            JOIN platform.project_roles pr ON pr.id = pm.project_role_id
            WHERE pm.user_id = @userId AND pm.status = 'active'
            """;
        return await conn.QueryAsync<AdminProjectMembershipDto>(sql, new { userId });
    }

    public async Task RevokeAllTokensAsync(Guid userId)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.refresh_tokens SET revoked = true WHERE user_id = @userId",
            new { userId });
        await conn.ExecuteAsync(
            "UPDATE platform.claude_tokens SET revoked = true WHERE user_id = @userId",
            new { userId });
    }
}
