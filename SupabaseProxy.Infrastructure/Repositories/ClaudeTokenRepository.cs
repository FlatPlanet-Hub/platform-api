using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;
using SupabaseProxy.Infrastructure.Configuration;

namespace SupabaseProxy.Infrastructure.Repositories;

public sealed class ClaudeTokenRepository : IClaudeTokenRepository
{
    private readonly SupabaseSettings _settings;

    public ClaudeTokenRepository(IOptions<SupabaseSettings> settings) => _settings = settings.Value;

    private NpgsqlConnection CreateConnection() => new(_settings.BuildConnectionString());

    public async Task<ClaudeToken> CreateAsync(ClaudeToken token)
    {
        await using var conn = CreateConnection();
        const string sql = """
            INSERT INTO platform.claude_tokens (id, user_id, project_id, token_hash, expires_at, revoked, created_at)
            VALUES (@Id, @UserId, @ProjectId, @TokenHash, @ExpiresAt, @Revoked, @CreatedAt)
            RETURNING *
            """;
        return await conn.QuerySingleAsync<ClaudeToken>(sql, token);
    }

    public async Task<IEnumerable<ClaudeToken>> GetActiveByUserIdAsync(Guid userId)
    {
        await using var conn = CreateConnection();
        return await conn.QueryAsync<ClaudeToken>(
            "SELECT * FROM platform.claude_tokens WHERE user_id = @userId AND revoked = false AND expires_at > now() ORDER BY created_at DESC",
            new { userId });
    }

    public async Task<ClaudeToken?> GetByIdAsync(Guid tokenId)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ClaudeToken>(
            "SELECT * FROM platform.claude_tokens WHERE id = @tokenId", new { tokenId });
    }

    public async Task<ClaudeToken?> GetByHashAsync(string tokenHash)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ClaudeToken>(
            "SELECT * FROM platform.claude_tokens WHERE token_hash = @tokenHash AND revoked = false",
            new { tokenHash });
    }

    public async Task RevokeAsync(Guid tokenId)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.claude_tokens SET revoked = true WHERE id = @tokenId",
            new { tokenId });
    }
}
