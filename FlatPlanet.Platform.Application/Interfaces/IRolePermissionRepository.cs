using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IRolePermissionRepository
{
    Task AssignAsync(Guid roleId, IEnumerable<Guid> permissionIds, Guid? grantedBy);
    Task RemoveAsync(Guid roleId, Guid permissionId);
    Task<IEnumerable<RolePermission>> GetByRoleIdAsync(Guid roleId);
    Task<IEnumerable<string>> GetPermissionNamesByRoleIdAsync(Guid roleId);
}
