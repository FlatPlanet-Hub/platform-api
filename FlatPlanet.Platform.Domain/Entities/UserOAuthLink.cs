namespace FlatPlanet.Platform.Domain.Entities;

public sealed class UserOAuthLink
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid ProviderId { get; init; }
    public string ProviderUserId { get; init; } = string.Empty;
    public string? ProviderUsername { get; set; }
    public string? ProviderEmail { get; set; }
    public string? ProviderAvatarUrl { get; set; }
    public string? AccessTokenEncrypted { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}
