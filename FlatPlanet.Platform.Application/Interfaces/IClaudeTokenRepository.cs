using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IClaudeTokenRepository
{
    Task<ClaudeToken> CreateAsync(ClaudeToken token);
    Task<IEnumerable<ClaudeToken>> GetActiveByUserIdAsync(Guid userId);
    Task<ClaudeToken?> GetByIdAsync(Guid tokenId);
    Task<ClaudeToken?> GetByHashAsync(string tokenHash);
    Task RevokeAsync(Guid tokenId);
    Task RevokeAllByUserProjectAsync(Guid userId, Guid projectId);
}
