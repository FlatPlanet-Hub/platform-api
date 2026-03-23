namespace FlatPlanet.Platform.Domain.Entities;

public sealed class Company
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}
