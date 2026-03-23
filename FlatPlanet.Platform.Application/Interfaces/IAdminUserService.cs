using FlatPlanet.Platform.Application.DTOs.Admin;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IAdminUserService
{
    Task<AdminUserListResponse> ListUsersAsync(AdminUserListFilter filter);
    Task<AdminUserDto> GetUserAsync(Guid userId);
    Task<AdminUserDto> CreateUserAsync(Guid adminId, CreateAdminUserRequest request);
    Task<IEnumerable<AdminUserDto>> BulkCreateUsersAsync(Guid adminId, BulkCreateUsersRequest request);
    Task<AdminUserDto> UpdateUserAsync(Guid adminId, Guid userId, UpdateAdminUserRequest request);
    Task UpdateUserRolesAsync(Guid adminId, Guid userId, UpdateUserRolesRequest request);
    Task UpdateUserProjectRoleAsync(Guid adminId, Guid userId, Guid projectId, UpdateUserProjectRoleRequest request);
    Task DeactivateUserAsync(Guid adminId, Guid userId);
}
