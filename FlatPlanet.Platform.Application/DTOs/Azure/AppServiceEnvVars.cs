namespace FlatPlanet.Platform.Application.DTOs.Azure;

public sealed record AppServiceEnvVars(
    string JwtSecretKey,
    string JwtIssuer,
    string JwtAudience,
    string PlatformApiBaseUrl,
    string? PlatformApiToken,
    string SchemaName);
