using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;
using SupabaseProxy.Infrastructure.Configuration;

namespace SupabaseProxy.Infrastructure.Repositories;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly SupabaseSettings _settings;

    public RefreshTokenRepository(IOptions<SupabaseSettings> settings) => _settings = settings.Value;

    private NpgsqlConnection CreateConnection() => new(_settings.BuildConnectionString());

    public async Task<RefreshToken> CreateAsync(RefreshToken token)
    {
        await using var conn = CreateConnection();
        const string sql = """
            INSERT INTO platform.refresh_tokens (id, user_id, token_hash, expires_at, revoked, created_at)
            VALUES (@Id, @UserId, @TokenHash, @ExpiresAt, @Revoked, @CreatedAt)
            RETURNING *
            """;
        return await conn.QuerySingleAsync<RefreshToken>(sql, token);
    }

    public async Task<RefreshToken?> GetByHashAsync(string tokenHash)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<RefreshToken>(
            "SELECT * FROM platform.refresh_tokens WHERE token_hash = @tokenHash AND revoked = false",
            new { tokenHash });
    }

    public async Task RevokeAsync(Guid tokenId)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.refresh_tokens SET revoked = true WHERE id = @tokenId",
            new { tokenId });
    }

    public async Task RevokeAllForUserAsync(Guid userId)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.refresh_tokens SET revoked = true WHERE user_id = @userId",
            new { userId });
    }
}
