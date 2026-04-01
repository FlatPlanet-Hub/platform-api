using FlatPlanet.Platform.Application.DTOs.Auth;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IClaudeConfigService
{
    Task<ClaudeConfigResponse> GenerateAsync(Guid userId, Guid projectId, string baseUrl, string userName, string userEmail);
    Task<ClaudeConfigResponse> RegenerateAsync(Guid userId, Guid projectId, string baseUrl, string userName, string userEmail);
    Task RevokeAsync(Guid userId, Guid projectId);
    Task<IEnumerable<ClaudeTokenSummaryDto>> ListActiveTokensAsync(Guid userId);

    Task<WorkspaceResponse> GetWorkspaceAsync(Guid userId, Guid projectId, string baseUrl, string userName, string userEmail);

    /// <summary>
    /// Generates an API token, stores it in api_tokens, and returns the rendered CLAUDE.md content.
    /// Called during project creation to push CLAUDE.md to the repo.
    /// </summary>
    Task<string> RenderAndStoreTokenAsync(Project project, Guid userId, string actorEmail, string baseUrl);
}
