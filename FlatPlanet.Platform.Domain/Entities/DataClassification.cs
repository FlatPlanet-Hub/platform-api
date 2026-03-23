namespace FlatPlanet.Platform.Domain.Entities;

public sealed class DataClassification
{
    public Guid Id { get; init; }
    public Guid ResourceId { get; init; }
    public string Classification { get; set; } = string.Empty; // 'public', 'internal', 'confidential', 'restricted'
    public string? HandlingNotes { get; set; }
    public Guid? ClassifiedBy { get; init; }
    public DateTime ClassifiedAt { get; init; }
}
