using FlatPlanet.Platform.Application.DTOs.Auth;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IClaudeConfigService
{
    Task<ClaudeConfigResponse> GenerateAsync(Guid userId, Guid projectId, string baseUrl);
    Task<ClaudeConfigResponse> RegenerateAsync(Guid userId, Guid projectId, string baseUrl);
    Task RevokeAsync(Guid userId, Guid projectId);
    Task<IEnumerable<ClaudeTokenSummaryDto>> ListActiveTokensAsync(Guid userId);
}
