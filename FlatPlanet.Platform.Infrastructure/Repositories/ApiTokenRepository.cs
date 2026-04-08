using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class ApiTokenRepository(IDbConnectionFactory connectionFactory) : IApiTokenRepository
{
    public async Task<ApiToken> CreateAsync(ApiToken token)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync("""
            INSERT INTO platform.api_tokens
                (id, user_id, app_id, name, token_hash, permissions, expires_at, revoked, created_at)
            VALUES
                (@Id, @UserId, @AppId, @Name, @TokenHash, @Permissions, @ExpiresAt, @Revoked, @CreatedAt)
            """, token);
        return token;
    }

    public async Task<ApiToken?> GetByIdAsync(Guid id)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ApiToken>(
            "SELECT * FROM platform.api_tokens WHERE id = @Id AND revoked = false", new { Id = id });
    }

    public async Task<ApiToken?> GetByHashAsync(string tokenHash)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ApiToken>(
            "SELECT * FROM platform.api_tokens WHERE token_hash = @Hash AND revoked = false AND expires_at > now()",
            new { Hash = tokenHash });
    }

    public async Task<IEnumerable<ApiToken>> GetActiveByUserIdAsync(Guid userId)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<ApiToken>(
            "SELECT * FROM platform.api_tokens WHERE user_id = @UserId AND revoked = false AND expires_at > now() ORDER BY created_at DESC",
            new { UserId = userId });
    }

    public async Task RevokeAsync(Guid id, string? reason = null)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.api_tokens SET revoked = true, revoked_reason = @Reason WHERE id = @Id",
            new { Id = id, Reason = reason });
    }

    public async Task UpdateLastUsedAsync(Guid id)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.api_tokens SET last_used_at = now() WHERE id = @Id",
            new { Id = id });
    }
}
