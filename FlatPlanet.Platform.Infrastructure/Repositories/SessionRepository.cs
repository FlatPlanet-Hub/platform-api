using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class SessionRepository(IDbConnectionFactory connectionFactory) : ISessionRepository
{
    public async Task<Session> CreateAsync(Session session)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync("""
            INSERT INTO platform.sessions
                (id, user_id, app_id, ip_address, user_agent, started_at, last_active_at, expires_at, is_active)
            VALUES
                (@Id, @UserId, @AppId, @IpAddress, @UserAgent, @StartedAt, @LastActiveAt, @ExpiresAt, @IsActive)
            """, session);
        return session;
    }

    public async Task<Session?> GetByIdAsync(Guid id)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Session>(
            "SELECT * FROM platform.sessions WHERE id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<Session>> GetActiveByUserIdAsync(Guid userId)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<Session>(
            "SELECT * FROM platform.sessions WHERE user_id = @UserId AND is_active = true ORDER BY started_at DESC",
            new { UserId = userId });
    }

    public async Task UpdateLastActiveAsync(Guid id)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.sessions SET last_active_at = now() WHERE id = @Id",
            new { Id = id });
    }

    public async Task EndAsync(Guid id, string reason)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.sessions SET is_active = false, ended_reason = @Reason WHERE id = @Id",
            new { Id = id, Reason = reason });
    }

    public async Task EndAllForUserAsync(Guid userId, string reason)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.sessions SET is_active = false, ended_reason = @Reason WHERE user_id = @UserId AND is_active = true",
            new { UserId = userId, Reason = reason });
    }
}
