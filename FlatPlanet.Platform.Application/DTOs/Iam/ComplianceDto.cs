namespace FlatPlanet.Platform.Application.DTOs.Iam;

public sealed class SetClassificationRequest
{
    public string Classification { get; init; } = string.Empty;
    public string? HandlingNotes { get; init; }
}

public sealed class RecordConsentRequest
{
    public string ConsentType { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public bool Consented { get; init; }
}

public sealed class ReportIncidentRequest
{
    public string Severity { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Guid? AffectedAppId { get; init; }
    public int? AffectedUsersCount { get; init; }
}

public sealed class UpdateIncidentRequest
{
    public string? Status { get; init; }
    public string? Resolution { get; init; }
}

public sealed class IncidentDto
{
    public Guid Id { get; init; }
    public string Severity { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Resolution { get; init; }
    public DateTime ReportedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
}

public sealed class RecordVerificationRequest
{
    public string Method { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string? RecordingReference { get; init; }
    public string? Notes { get; init; }
}
