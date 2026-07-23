namespace FlatPlanet.Platform.Infrastructure.Configuration;

public sealed class GitHubSettings
{
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
    public string FrontendCallbackUrl { get; init; } = string.Empty;
    public string ServiceToken { get; init; } = string.Empty;
    // Separate token with `workflow` scope — used only for pushing .github/workflows/ files.
    // Falls back to ServiceToken if not set (for local dev / backwards compat).
    public string WorkflowToken { get; init; } = string.Empty;
    public string OrgName { get; init; } = string.Empty;
}
