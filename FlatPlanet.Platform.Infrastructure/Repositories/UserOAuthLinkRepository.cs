using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class UserOAuthLinkRepository(IDbConnectionFactory connectionFactory) : IUserOAuthLinkRepository
{
    public async Task<UserOAuthLink?> GetByProviderUserIdAsync(Guid providerId, string providerUserId)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<UserOAuthLink>(
            "SELECT * FROM platform.user_oauth_links WHERE provider_id = @ProviderId AND provider_user_id = @ProviderUserId",
            new { ProviderId = providerId, ProviderUserId = providerUserId });
    }

    public async Task<IEnumerable<UserOAuthLink>> GetByUserIdAsync(Guid userId)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<UserOAuthLink>(
            "SELECT * FROM platform.user_oauth_links WHERE user_id = @UserId",
            new { UserId = userId });
    }

    public async Task<UserOAuthLink> CreateAsync(UserOAuthLink link)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync("""
            INSERT INTO platform.user_oauth_links
                (id, user_id, provider_id, provider_user_id, provider_username, provider_email,
                 provider_avatar_url, access_token_encrypted, created_at, updated_at)
            VALUES
                (@Id, @UserId, @ProviderId, @ProviderUserId, @ProviderUsername, @ProviderEmail,
                 @ProviderAvatarUrl, @AccessTokenEncrypted, @CreatedAt, @UpdatedAt)
            """, link);
        return link;
    }

    public async Task UpdateAccessTokenAsync(Guid id, string accessTokenEncrypted)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.user_oauth_links SET access_token_encrypted = @Token, updated_at = now() WHERE id = @Id",
            new { Id = id, Token = accessTokenEncrypted });
    }

    public async Task DeleteAsync(Guid id)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM platform.user_oauth_links WHERE id = @Id", new { Id = id });
    }
}
