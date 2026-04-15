namespace FlatPlanet.Platform.Application.DTOs.Storage;

public sealed record StorageProvisionResponse(
    string BucketName,
    DateTime ProvisionedAt);
