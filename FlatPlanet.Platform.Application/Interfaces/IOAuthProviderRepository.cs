using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IOAuthProviderRepository
{
    Task<OAuthProvider?> GetByNameAsync(string name);
    Task<IEnumerable<OAuthProvider>> GetAllEnabledAsync();
}
