using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
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
            INSERT INTO platform.users (id, github_id, github_username, email, display_name, avatar_url, github_access_token, is_active, created_at, updated_at)
            VALUES (@Id, @GitHubId, @GitHubUsername, @Email, @DisplayName, @AvatarUrl, @GitHubAccessToken, @IsActive, @CreatedAt, @UpdatedAt)
            RETURNING *
            """;
        return await conn.QuerySingleAsync<User>(sql, user);
    }

    public async Task UpdateAsync(User user)
    {
        await using var conn = CreateConnection();
        const string sql = """
            UPDATE platform.users
            SET github_username = @GitHubUsername, email = @Email, display_name = @DisplayName,
                avatar_url = @AvatarUrl, github_access_token = @GitHubAccessToken,
                is_active = @IsActive, updated_at = @UpdatedAt
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
}
