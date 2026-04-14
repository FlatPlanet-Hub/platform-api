namespace FlatPlanet.Platform.Domain.Entities;

public class PlatformFile
{
    public Guid Id { get; init; }
    public Guid? AppId { get; init; }
    public string BusinessCode { get; init; } = string.Empty;
    public string Category { get; init; } = "general";
    public string OriginalName { get; init; } = string.Empty;
    public string BlobName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public Guid UploadedBy { get; init; }
    public string[] Tags { get; init; } = [];
    public bool IsDeleted { get; init; }
    public DateTime? DeletedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}
