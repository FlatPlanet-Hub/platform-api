namespace FlatPlanet.Platform.Domain.Entities;

public sealed class UserAppRole
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid AppId { get; init; }
    public Guid RoleId { get; init; }
    public Guid? GrantedBy { get; init; }
    public DateTime GrantedAt { get; init; }
    public DateTime? ExpiresAt { get; set; }
    public string Status { get; set; } = "active";
}
