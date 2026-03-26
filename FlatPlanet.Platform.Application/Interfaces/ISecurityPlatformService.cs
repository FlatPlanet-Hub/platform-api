using FlatPlanet.Platform.Application.DTOs.Security;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface ISecurityPlatformService
{
    Task<Guid> RegisterAppAsync(string name, string slug, string baseUrl, Guid companyId);
    Task GrantRoleAsync(Guid appId, Guid userId, string roleName);
    Task RevokeRoleAsync(Guid appId, Guid userId);
    Task<IEnumerable<UserAppRoleDto>> GetUserAppRolesAsync(Guid userId);
    Task<IEnumerable<AppMemberDto>> GetAppMembersAsync(Guid appId);
    Task<SecurityPlatformUserDto> GetUserAsync(Guid userId);
    Task<bool> AuthorizeAsync(string appSlug, string resourceIdentifier, string requiredPermission, Guid userId);
}
