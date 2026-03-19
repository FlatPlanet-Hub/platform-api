using SupabaseProxy.Application.DTOs.Admin;
using SupabaseProxy.Domain.Entities;

namespace SupabaseProxy.Application.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);
    Task<User?> GetByGitHubIdAsync(long githubId);
    Task<User?> GetByGitHubUsernameAsync(string username);
    Task<User> CreateAsync(User user);
    Task UpdateAsync(User user);
    Task<IEnumerable<string>> GetSystemRolesAsync(Guid userId);
    Task<IEnumerable<Role>> GetSystemRoleEntitiesAsync(Guid userId);
    Task AssignSystemRoleAsync(Guid userId, Guid roleId, Guid assignedBy);
    Task RevokeSystemRoleAsync(Guid userId, Guid roleId);
    Task RevokeAllSystemRolesAsync(Guid userId);
    Task SetSystemRolesAsync(Guid userId, IEnumerable<Guid> roleIds, Guid assignedBy);

    // Admin list with pagination/filtering
    Task<(IEnumerable<User> Users, int TotalCount)> ListAsync(AdminUserListFilter filter);

    // Project memberships for admin view
    Task<IEnumerable<AdminProjectMembershipDto>> GetProjectMembershipsAsync(Guid userId);

    // Revoke all tokens (for deactivation cascade)
    Task RevokeAllTokensAsync(Guid userId);
}
