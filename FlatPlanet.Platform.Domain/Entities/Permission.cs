namespace FlatPlanet.Platform.Domain.Entities;

public sealed class Permission
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Category { get; init; } = string.Empty;
}
