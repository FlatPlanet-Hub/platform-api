using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IDbConnectionFactory _db;

    public RefreshTokenRepository(IDbConnectionFactory db) => _db = db;

    public async Task<RefreshToken> CreateAsync(RefreshToken token)
    {
        await using var conn = _db.CreateConnection();
        const string sql = """
            INSERT INTO platform.refresh_tokens (id, user_id, token_hash, expires_at, revoked, created_at)
            VALUES (@Id, @UserId, @TokenHash, @ExpiresAt, @Revoked, @CreatedAt)
            RETURNING *
            """;
        return await conn.QuerySingleAsync<RefreshToken>(sql, token);
    }

    public async Task<RefreshToken?> GetByHashAsync(string tokenHash)
    {
        await using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<RefreshToken>(
            "SELECT * FROM platform.refresh_tokens WHERE token_hash = @tokenHash AND revoked = false",
            new { tokenHash });
    }

    public async Task RevokeAsync(Guid tokenId)
    {
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.refresh_tokens SET revoked = true WHERE id = @tokenId",
            new { tokenId });
    }

    public async Task RevokeAllForUserAsync(Guid userId)
    {
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.refresh_tokens SET revoked = true WHERE user_id = @userId",
            new { userId });
    }
}
