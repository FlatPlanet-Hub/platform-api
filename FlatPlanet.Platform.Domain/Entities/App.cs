namespace FlatPlanet.Platform.Domain.Entities;

/// <summary>Registered application in the IAM platform.</summary>
public sealed class App
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? SchemaName { get; set; }
    public string Status { get; set; } = "active";
    public Guid? RegisteredBy { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}
