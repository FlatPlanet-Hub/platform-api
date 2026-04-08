namespace FlatPlanet.Platform.Application.DTOs.Azure;

public sealed record ProvisionAzureResponse(
    string AppServiceName,
    string AppServiceUrl,
    string? PlatformApiToken);
