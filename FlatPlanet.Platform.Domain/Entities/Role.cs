namespace FlatPlanet.Platform.Domain.Entities;

public sealed class Role
{
    public Guid Id { get; init; }
    public Guid? AppId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsSystem { get; init; }
    public bool IsPlatformRole { get; init; }
    public DateTime CreatedAt { get; init; }
}
