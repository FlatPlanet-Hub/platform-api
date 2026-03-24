namespace FlatPlanet.Platform.Application.DTOs.Auth;

public sealed class ClaudeTokenResponse
{
    public Guid TokenId { get; init; }
    public string Token { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public McpConfigDto McpConfig { get; init; } = new();
}

public sealed class McpConfigDto
{
    public Dictionary<string, McpServerDto> McpServers { get; init; } = [];
}

public sealed class McpServerDto
{
    public string Command { get; init; } = string.Empty;
    public string[] Args { get; init; } = [];
    public Dictionary<string, string> Env { get; init; } = [];
}
