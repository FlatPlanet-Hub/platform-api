using FlatPlanet.Platform.Application.DTOs.Auth;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IUserService
{
    Task<User> UpsertFromGitHubAsync(GitHubUserProfile profile);
    Task<UserProfileResponse> GetProfileAsync(Guid userId);
    Task<IEnumerable<UserProjectSummaryDto>> GetUserProjectsForTokenAsync(Guid userId);
    Task AssignSystemRoleAsync(Guid requestingUserId, RoleAssignRequest request);
    Task RevokeSystemRoleAsync(Guid requestingUserId, RoleRevokeRequest request);
    Task<IEnumerable<Domain.Entities.Role>> GetSystemRolesAsync();
    Task<IEnumerable<string>> GetEffectivePermissionsAsync(Guid userId, IEnumerable<string> systemRoles);
}
