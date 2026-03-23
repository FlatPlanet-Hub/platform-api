namespace FlatPlanet.Platform.Domain.Entities;

public sealed class RolePermission
{
    public Guid Id { get; init; }
    public Guid RoleId { get; init; }
    public Guid PermissionId { get; init; }
    public Guid? GrantedBy { get; init; }
    public DateTime CreatedAt { get; init; }
}
