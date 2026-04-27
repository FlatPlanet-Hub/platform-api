namespace FlatPlanet.Platform.Application.DTOs.Azure;

public sealed record SyncCorsResponse(
    string AppServiceName,
    string AllowedOrigin,
    string Message);
