namespace FlatPlanet.Platform.Infrastructure.Configuration;

public sealed class GitHubSettings
{
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
    public string FrontendCallbackUrl { get; init; } = string.Empty;
    public string ServiceToken { get; init; } = string.Empty;
    public string OrgName { get; init; } = string.Empty;
}
