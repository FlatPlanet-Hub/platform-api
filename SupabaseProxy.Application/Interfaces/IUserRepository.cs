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
    Task AssignSystemRoleAsync(Guid userId, Guid roleId, Guid assignedBy);
    Task RevokeSystemRoleAsync(Guid userId, Guid roleId);
}
