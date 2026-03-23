using FlatPlanet.Platform.Application.DTOs.Admin;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IAdminRoleService
{
    Task<IEnumerable<AdminRoleDto>> ListRolesAsync();
    Task<AdminRoleDto> CreateRoleAsync(Guid adminId, CreateCustomRoleRequest request);
    Task<AdminRoleDto> UpdateRoleAsync(Guid adminId, Guid roleId, UpdateCustomRoleRequest request);
    Task DeactivateRoleAsync(Guid adminId, Guid roleId);
}
