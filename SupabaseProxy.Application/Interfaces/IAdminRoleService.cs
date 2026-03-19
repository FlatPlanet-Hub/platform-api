using SupabaseProxy.Application.DTOs.Admin;

namespace SupabaseProxy.Application.Interfaces;

public interface IAdminRoleService
{
    Task<IEnumerable<AdminRoleDto>> ListRolesAsync();
    Task<AdminRoleDto> CreateRoleAsync(Guid adminId, CreateCustomRoleRequest request);
    Task<AdminRoleDto> UpdateRoleAsync(Guid adminId, Guid roleId, UpdateCustomRoleRequest request);
    Task DeactivateRoleAsync(Guid adminId, Guid roleId);
}
