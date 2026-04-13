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
    /// Generates an API token, stores it in api_tokens, and returns both the raw token and the
    /// rendered CLAUDE-local.md content. Called during project creation and Azure provisioning.
    /// </summary>
    Task<(string RawToken, string RenderedMarkdown)> RenderAndStoreTokenAsync(Project project, Guid userId, string actorEmail, string baseUrl);

    /// <summary>
    /// Returns the current template version constant embedded in the service.
    /// Used by Claude agents to detect whether their local CLAUDE-local.md is outdated.
    /// </summary>
    ClaudeConfigVersionResponse GetTemplateVersion();
}
