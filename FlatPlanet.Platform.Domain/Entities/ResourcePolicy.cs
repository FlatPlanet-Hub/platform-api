namespace FlatPlanet.Platform.Domain.Entities;

public sealed class ResourcePolicy
{
    public Guid Id { get; init; }
    public Guid ResourceId { get; init; }
    public string PolicyKey { get; init; } = string.Empty;
    public string PolicyValue { get; init; } = string.Empty; // JSONB stored as string
    public Guid? CreatedBy { get; init; }
}
