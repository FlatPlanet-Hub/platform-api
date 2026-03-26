namespace FlatPlanet.Platform.Domain.Entities;

public sealed class Project
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SchemaName { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public Guid? AppId { get; set; }
    public string? AppSlug { get; set; }
    public string? GitHubRepo { get; set; }
    public string? TechStack { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}
