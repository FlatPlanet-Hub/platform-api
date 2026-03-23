namespace FlatPlanet.Platform.Domain.Entities;

public sealed class Resource
{
    public Guid Id { get; init; }
    public Guid AppId { get; set; }
    public Guid ResourceTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; init; }
}
