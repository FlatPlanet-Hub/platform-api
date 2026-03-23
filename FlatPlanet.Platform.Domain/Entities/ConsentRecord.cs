namespace FlatPlanet.Platform.Domain.Entities;

public sealed class ConsentRecord
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string ConsentType { get; init; } = string.Empty; // 'terms_of_service', 'privacy_policy', 'data_processing', 'marketing'
    public string Version { get; init; } = string.Empty;
    public bool Consented { get; init; }
    public string? IpAddress { get; init; }
    public DateTime ConsentedAt { get; init; }
    public DateTime? WithdrawnAt { get; set; }
}
