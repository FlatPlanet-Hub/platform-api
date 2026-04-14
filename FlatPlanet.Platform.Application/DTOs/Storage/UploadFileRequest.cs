namespace FlatPlanet.Platform.Application.DTOs.Storage;

public sealed record UploadFileRequest(
    string BusinessCode,
    string Category,
    string[] Tags,
    Guid? AppId = null);
