using FlatPlanet.Platform.Application.DTOs.SecurityPlatform;
using FlatPlanet.Platform.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class CachedSecurityPlatformService : ISecurityPlatformService
{
    private readonly SecurityPlatformService _inner;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public CachedSecurityPlatformService(SecurityPlatformService inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<IEnumerable<SpAppAccessDto>> GetUserAppAccessAsync(Guid userId)
    {
        var key = CacheKey(userId);
        if (_cache.TryGetValue(key, out IEnumerable<SpAppAccessDto>? cached) && cached is not null)
            return cached;

        var result = await _inner.GetUserAppAccessAsync(userId);
        _cache.Set(key, result, CacheTtl);
        return result;
    }

    public Task GrantRoleAsync(Guid appId, Guid userId, string roleName)
    {
        _cache.Remove(CacheKey(userId));
        return _inner.GrantRoleAsync(appId, userId, roleName);
    }

    public Task ChangeRoleAsync(Guid appId, Guid userId, string roleName)
    {
        _cache.Remove(CacheKey(userId));
        return _inner.ChangeRoleAsync(appId, userId, roleName);
    }

    public Task RevokeRoleAsync(Guid appId, Guid userId)
    {
        _cache.Remove(CacheKey(userId));
        return _inner.RevokeRoleAsync(appId, userId);
    }

    public Task<Guid> RegisterAppAsync(string name, string slug, string baseUrl, Guid companyId)
        => _inner.RegisterAppAsync(name, slug, baseUrl, companyId);

    public Task DeactivateAppAsync(Guid appId, string newName, string newSlug)
        => _inner.DeactivateAppAsync(appId, newName, newSlug);

    public Task SetupProjectRolesAsync(Guid appId)
        => _inner.SetupProjectRolesAsync(appId);

    public Task<Guid?> GetAppIdBySlugAsync(string slug)
        => _inner.GetAppIdBySlugAsync(slug);

    public Task<SpUserDto> GetUserAsync(Guid userId)
        => _inner.GetUserAsync(userId);

    public Task<IEnumerable<SpAppMemberDto>> GetAppMembersAsync(Guid appId)
        => _inner.GetAppMembersAsync(appId);

    public Task<bool> AuthorizeAsync(string appSlug, string resourceIdentifier, string requiredPermission)
        => _inner.AuthorizeAsync(appSlug, resourceIdentifier, requiredPermission);

    private static string CacheKey(Guid userId) => $"sp_access_{userId}";
}
