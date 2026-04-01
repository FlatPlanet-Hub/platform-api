namespace FlatPlanet.Platform.Application.DTOs.Auth;

public sealed class WorkspaceResponse
{
    public string Content { get; init; } = string.Empty;
    public string Filename { get; init; } = "CLAUDE-local.md";
    public string GitignoreEntry { get; init; } = "CLAUDE-local.md";
    public Guid TokenId { get; init; }
    public DateTime ExpiresAt { get; init; }
}
