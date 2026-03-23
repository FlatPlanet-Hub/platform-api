using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class OAuthProviderRepository(IDbConnectionFactory connectionFactory) : IOAuthProviderRepository
{
    public async Task<OAuthProvider?> GetByNameAsync(string name)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<OAuthProvider>(
            "SELECT * FROM platform.oauth_providers WHERE name = @Name", new { Name = name });
    }

    public async Task<IEnumerable<OAuthProvider>> GetAllEnabledAsync()
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<OAuthProvider>(
            "SELECT * FROM platform.oauth_providers WHERE is_enabled = true ORDER BY name");
    }
}
