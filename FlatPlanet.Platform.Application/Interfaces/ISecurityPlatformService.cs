using FlatPlanet.Platform.Application.DTOs.SecurityPlatform;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface ISecurityPlatformService
{
    Task<Guid> RegisterAppAsync(string name, string slug, string baseUrl, Guid companyId);
    Task SetupProjectRolesAsync(Guid appId);

    Task GrantRoleAsync(Guid appId, Guid userId, string roleName);
    Task ChangeRoleAsync(Guid appId, Guid userId, string roleName);
    Task RevokeRoleAsync(Guid appId, Guid userId);

    Task<SpUserDto> GetUserAsync(Guid userId);
    Task<IEnumerable<SpAppAccessDto>> GetUserAppAccessAsync(Guid userId);
    Task<IEnumerable<SpAppMemberDto>> GetAppMembersAsync(Guid appId);

    // Uses caller's JWT (not service token) — SP derives userId from the bearer token
    Task<bool> AuthorizeAsync(string appSlug, string resourceIdentifier, string requiredPermission);
}
