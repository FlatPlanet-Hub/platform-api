namespace FlatPlanet.Platform.Application.Common.Options;

public sealed class GitHubOptions
{
    public string ServiceToken { get; init; } = string.Empty;
    public string OrgName { get; init; } = string.Empty;
}
