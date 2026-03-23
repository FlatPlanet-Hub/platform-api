namespace FlatPlanet.Platform.Application.DTOs.Iam;

public sealed class CreateApiTokenRequest
{
    public string Name { get; init; } = string.Empty;
    public Guid? AppId { get; init; }
    public string[] Permissions { get; init; } = [];
    public int ExpiryDays { get; init; } = 30;
}

public sealed class ApiTokenResponse
{
    public Guid TokenId { get; init; }
    public string Token { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string[] Permissions { get; init; } = [];
    public DateTime ExpiresAt { get; init; }
    public McpConfigDto? McpConfig { get; init; }
}

public sealed class ApiTokenSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid? AppId { get; init; }
    public string[] Permissions { get; init; } = [];
    public DateTime ExpiresAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public DateTime CreatedAt { get; init; }
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
