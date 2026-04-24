namespace FlatPlanet.Platform.Infrastructure.Configuration;

public sealed class NetlifySettings
{
    public string ApiToken { get; init; } = string.Empty;
    public string ApiBaseUrl { get; init; } = "https://api.netlify.com/api/v1";
}
