namespace FlatPlanet.Platform.Application.DTOs.Auth;

public sealed class ClaudeConfigResponse
{
    public string Content { get; init; } = string.Empty;
    public Guid TokenId { get; init; }
    public DateTime ExpiresAt { get; init; }
}

public sealed class ClaudeTokenSummaryDto
{
    public Guid TokenId { get; init; }
    public Guid ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public DateTime CreatedAt { get; init; }
}
