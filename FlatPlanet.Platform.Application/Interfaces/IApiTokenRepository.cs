using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IApiTokenRepository
{
    Task<ApiToken> CreateAsync(ApiToken token);
    Task<ApiToken?> GetByIdAsync(Guid id);
    Task<ApiToken?> GetByHashAsync(string tokenHash);
    Task<IEnumerable<ApiToken>> GetActiveByUserIdAsync(Guid userId);
    Task<IEnumerable<ApiToken>> GetActiveByAppIdAsync(Guid appId);
    Task RevokeAsync(Guid id, string? reason = null);
    Task UpdateLastUsedAsync(Guid id);
}
