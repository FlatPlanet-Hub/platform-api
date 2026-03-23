namespace FlatPlanet.Platform.Domain.Entities;

public sealed class CustomRole
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string[] Permissions { get; set; } = [];
    public Guid? CreatedBy { get; init; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}
