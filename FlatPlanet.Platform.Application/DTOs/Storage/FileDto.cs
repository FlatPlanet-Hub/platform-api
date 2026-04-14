namespace FlatPlanet.Platform.Application.DTOs.Storage;

public sealed record FileDto(
    Guid FileId,
    Guid? AppId,
    string BusinessCode,
    string Category,
    string OriginalName,
    string ContentType,
    long FileSizeBytes,
    string[] Tags,
    string SasUrl,
    DateTime SasExpiresAt,
    DateTime CreatedAt);
