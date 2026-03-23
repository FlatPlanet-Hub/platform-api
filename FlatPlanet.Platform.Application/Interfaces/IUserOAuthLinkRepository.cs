using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IUserOAuthLinkRepository
{
    Task<UserOAuthLink?> GetByProviderUserIdAsync(Guid providerId, string providerUserId);
    Task<IEnumerable<UserOAuthLink>> GetByUserIdAsync(Guid userId);
    Task<UserOAuthLink> CreateAsync(UserOAuthLink link);
    Task UpdateAccessTokenAsync(Guid id, string accessTokenEncrypted);
    Task DeleteAsync(Guid id);
}
