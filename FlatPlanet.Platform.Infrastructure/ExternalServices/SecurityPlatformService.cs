using System.Net.Http.Json;
using FlatPlanet.Platform.Application.DTOs.Security;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class SecurityPlatformService : ISecurityPlatformService
{
    private readonly HttpClient _http;

    public SecurityPlatformService(IHttpClientFactory httpClientFactory)
    {
        _http = httpClientFactory.CreateClient("SecurityPlatform");
    }

    public async Task<Guid> RegisterAppAsync(string name, string slug, string baseUrl, Guid companyId)
    {
        var response = await _http.PostAsJsonAsync("api/v1/apps", new
        {
            name,
            slug,
            baseUrl,
            companyId
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RegisterAppResponse>();
        return result!.AppId;
    }

    public async Task GrantRoleAsync(Guid appId, Guid userId, string roleName)
    {
        var response = await _http.PostAsJsonAsync($"api/v1/apps/{appId}/users", new
        {
            userId,
            roleName
        });
        response.EnsureSuccessStatusCode();
    }

    public async Task RevokeRoleAsync(Guid appId, Guid userId)
    {
        var response = await _http.DeleteAsync($"api/v1/apps/{appId}/users/{userId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<IEnumerable<UserAppRoleDto>> GetUserAppRolesAsync(Guid userId)
    {
        var result = await _http.GetFromJsonAsync<IEnumerable<UserAppRoleDto>>($"api/v1/users/{userId}/roles");
        return result ?? [];
    }

    public async Task<IEnumerable<AppMemberDto>> GetAppMembersAsync(Guid appId)
    {
        var result = await _http.GetFromJsonAsync<IEnumerable<AppMemberDto>>($"api/v1/apps/{appId}/users");
        return result ?? [];
    }

    public async Task<SecurityPlatformUserDto> GetUserAsync(Guid userId)
    {
        var result = await _http.GetFromJsonAsync<SecurityPlatformUserDto>($"api/v1/users/{userId}");
        return result ?? throw new KeyNotFoundException($"User {userId} not found in Security Platform.");
    }

    public async Task<bool> AuthorizeAsync(string appSlug, string resourceIdentifier, string requiredPermission, Guid userId)
    {
        var response = await _http.PostAsJsonAsync("api/v1/authorize", new
        {
            appSlug,
            resourceIdentifier,
            requiredPermission,
            userId
        });
        if (!response.IsSuccessStatusCode) return false;
        var result = await response.Content.ReadFromJsonAsync<AuthorizeResponse>();
        return result?.Allowed ?? false;
    }

    private sealed record RegisterAppResponse(Guid AppId);
    private sealed record AuthorizeResponse(bool Allowed);
}
