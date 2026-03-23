namespace FlatPlanet.Platform.Domain.Entities;

public sealed class UserMfa
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string Method { get; init; } = string.Empty; // 'totp', 'sms', 'email'
    public string? SecretEncrypted { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsVerified { get; set; }
    public string[]? BackupCodesHash { get; set; }
    public DateTime EnrolledAt { get; init; }
    public DateTime? VerifiedAt { get; set; }
}
