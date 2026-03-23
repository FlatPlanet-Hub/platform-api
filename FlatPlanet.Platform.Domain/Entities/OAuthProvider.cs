namespace FlatPlanet.Platform.Domain.Entities;

public sealed class OAuthProvider
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; init; }
}
