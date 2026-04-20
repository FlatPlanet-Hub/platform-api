namespace FlatPlanet.Platform.Infrastructure.Configuration;

public sealed class DataverseSettings
{
    public string TokenUrl { get; init; } = string.Empty;
    public string ApiBaseUrl { get; init; } = string.Empty;
    public string TokenFunctionKey { get; init; } = string.Empty;
}
