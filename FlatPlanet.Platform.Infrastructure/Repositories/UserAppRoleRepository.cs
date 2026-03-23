using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class UserAppRoleRepository(IDbConnectionFactory connectionFactory) : IUserAppRoleRepository
{
    public async Task<UserAppRole> GrantAsync(UserAppRole userAppRole)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync("""
            INSERT INTO platform.user_app_roles
                (id, user_id, app_id, role_id, granted_by, granted_at, expires_at, status)
            VALUES
                (@Id, @UserId, @AppId, @RoleId, @GrantedBy, @GrantedAt, @ExpiresAt, @Status)
            ON CONFLICT (user_id, app_id, role_id) DO UPDATE
                SET status = 'active', expires_at = EXCLUDED.expires_at, granted_by = EXCLUDED.granted_by
            """, userAppRole);
        return userAppRole;
    }

    public async Task<UserAppRole?> GetAsync(Guid userId, Guid appId, Guid roleId)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<UserAppRole>(
            "SELECT * FROM platform.user_app_roles WHERE user_id = @UserId AND app_id = @AppId AND role_id = @RoleId",
            new { UserId = userId, AppId = appId, RoleId = roleId });
    }

    public async Task<IEnumerable<UserAppRole>> GetByUserAndAppAsync(Guid userId, Guid appId)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<UserAppRole>(
            "SELECT * FROM platform.user_app_roles WHERE user_id = @UserId AND app_id = @AppId AND status = 'active'",
            new { UserId = userId, AppId = appId });
    }

    public async Task<IEnumerable<UserAppRole>> GetByAppAsync(Guid appId)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<UserAppRole>(
            "SELECT * FROM platform.user_app_roles WHERE app_id = @AppId AND status = 'active'",
            new { AppId = appId });
    }

    public async Task<IEnumerable<UserAppRole>> GetByUserAsync(Guid userId)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<UserAppRole>(
            "SELECT * FROM platform.user_app_roles WHERE user_id = @UserId AND status = 'active'",
            new { UserId = userId });
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.user_app_roles SET status = @Status WHERE id = @Id",
            new { Id = id, Status = status });
    }

    public async Task RevokeAsync(Guid userId, Guid appId)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.user_app_roles SET status = 'revoked' WHERE user_id = @UserId AND app_id = @AppId",
            new { UserId = userId, AppId = appId });
    }

    public async Task ChangeRoleAsync(Guid userId, Guid appId, Guid newRoleId)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync("""
            UPDATE platform.user_app_roles
            SET role_id = @NewRoleId
            WHERE user_id = @UserId AND app_id = @AppId AND status = 'active'
            """, new { UserId = userId, AppId = appId, NewRoleId = newRoleId });
    }
}
