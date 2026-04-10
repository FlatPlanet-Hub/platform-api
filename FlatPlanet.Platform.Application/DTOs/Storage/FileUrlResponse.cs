namespace FlatPlanet.Platform.Application.DTOs.Storage;

public sealed record FileUrlResponse(string SasUrl, DateTime ExpiresAt);
