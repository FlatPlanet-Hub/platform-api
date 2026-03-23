namespace FlatPlanet.Platform.Domain.Entities;

public sealed class VerificationEvent
{
    public Guid Id { get; init; }
    public Guid VerifiedUserId { get; init; }
    public Guid VerifiedByUserId { get; init; }
    public string Method { get; init; } = string.Empty;   // 'video_call', 'in_person', 'document_check', 'manager_vouch'
    public string Outcome { get; init; } = string.Empty;  // 'verified', 'failed', 'inconclusive'
    public string? RecordingReference { get; init; }
    public string? Notes { get; init; }
    public DateTime VerifiedAt { get; init; }
}
