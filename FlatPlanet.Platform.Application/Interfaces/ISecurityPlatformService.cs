using FlatPlanet.Platform.Application.DTOs.SecurityPlatform;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface ISecurityPlatformService
{
    Task<Guid> RegisterAppAsync(string name, string slug, string baseUrl, Guid companyId);

    /// <summary>
    /// Renames the SP app's slug and name to the provided values and sets its status to inactive.
    /// Called on project deactivation so the original slug is freed for reuse.
    /// <para>
    /// <b>Important:</b> Pass the already-mutated (post-rename) values for <paramref name="newSlug"/>
    /// and <paramref name="newName"/> — not the originals. The SP <c>base_url</c> is preserved as-is.
    /// </para>
    /// <para>
    /// This call is non-transactional with the HubApi rename. Failures are logged by the caller
    /// but do not roll back the HubApi slug change.
    /// </para>
    /// </summary>
    Task DeactivateAppAsync(Guid appId, string newName, string newSlug);
    Task SetupProjectRolesAsync(Guid appId);

    Task GrantRoleAsync(Guid appId, Guid userId, string roleName);
    Task ChangeRoleAsync(Guid appId, Guid userId, string roleName);
    Task RevokeRoleAsync(Guid appId, Guid userId);

    Task<Guid?> GetAppIdBySlugAsync(string slug);
    Task<SpUserDto> GetUserAsync(Guid userId);
    Task<IEnumerable<SpAppAccessDto>> GetUserAppAccessAsync(Guid userId);
    Task<IEnumerable<SpAppMemberDto>> GetAppMembersAsync(Guid appId);

    // Uses caller's JWT (not service token) — SP derives userId from the bearer token
    Task<bool> AuthorizeAsync(string appSlug, string resourceIdentifier, string requiredPermission);
}
