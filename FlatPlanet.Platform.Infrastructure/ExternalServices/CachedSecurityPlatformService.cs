using FlatPlanet.Platform.Application.DTOs.SecurityPlatform;
using FlatPlanet.Platform.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class CachedSecurityPlatformService : ISecurityPlatformService
{
    private readonly SecurityPlatformService _inner;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CachedSecurityPlatformService> _logger;

    // Fresh TTL: how long before we attempt to re-fetch from SP under normal conditions.
    private static readonly TimeSpan FreshTtl = TimeSpan.FromMinutes(5);

    // Stale TTL: how long we keep the last known value as an outage fallback.
    // Covers Azure maintenance restarts (typically 2–5 min) with a large safety margin.
    private static readonly TimeSpan StaleTtl = TimeSpan.FromHours(2);

    public CachedSecurityPlatformService(
        SecurityPlatformService inner,
        IMemoryCache cache,
        ILogger<CachedSecurityPlatformService> logger)
    {
        _inner  = inner;
        _cache  = cache;
        _logger = logger;
    }

    public async Task<IEnumerable<SpAppAccessDto>> GetUserAppAccessAsync(Guid userId)
    {
        var freshKey = FreshKey(userId);
        var staleKey = StaleKey(userId);

        // Fresh cache hit — serve immediately, no SP call needed.
        if (_cache.TryGetValue(freshKey, out IEnumerable<SpAppAccessDto>? fresh) && fresh is not null)
            return fresh;

        try
        {
            var result = await _inner.GetUserAppAccessAsync(userId);

            // Store under both keys so stale fallback is always up to date.
            _cache.Set(freshKey, result, FreshTtl);
            _cache.Set(staleKey, result, StaleTtl);

            return result;
        }
        catch (Exception ex)
        {
            // SP is unreachable — serve the last known access list if we have one.
            // This keeps active users functional during Azure maintenance restarts.
            if (_cache.TryGetValue(staleKey, out IEnumerable<SpAppAccessDto>? stale) && stale is not null)
            {
                _logger.LogWarning(ex,
                    "SP unreachable for user {UserId} — serving stale access data.", userId);
                return stale;
            }

            // No stale data (first-ever request for this user while SP is down). Propagate.
            throw;
        }
    }

    public Task GrantRoleAsync(Guid appId, Guid userId, string roleName)
    {
        InvalidateUser(userId);
        return _inner.GrantRoleAsync(appId, userId, roleName);
    }

    public Task ChangeRoleAsync(Guid appId, Guid userId, string roleName)
    {
        InvalidateUser(userId);
        return _inner.ChangeRoleAsync(appId, userId, roleName);
    }

    public Task RevokeRoleAsync(Guid appId, Guid userId)
    {
        InvalidateUser(userId);
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

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Removes both fresh and stale cache entries for a user on explicit role changes.</summary>
    private void InvalidateUser(Guid userId)
    {
        _cache.Remove(FreshKey(userId));
        _cache.Remove(StaleKey(userId));
    }

    private static string FreshKey(Guid userId) => $"sp_access_fresh_{userId}";
    private static string StaleKey(Guid userId) => $"sp_access_stale_{userId}";
}
