using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FlatPlanet.Platform.Application.DTOs.SecurityPlatform;
using FlatPlanet.Platform.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class SecurityPlatformService : ISecurityPlatformService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SecurityPlatformService(
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor)
    {
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    private HttpClient ServiceClient => _httpClientFactory.CreateClient("SecurityPlatform");

    public async Task<Guid> RegisterAppAsync(string name, string slug, string baseUrl, Guid companyId)
    {
        var response = await ServiceClient.PostAsJsonAsync("api/v1/apps", new
        {
            companyId,
            name,
            slug,
            baseUrl
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SpResponse<SpAppIdData>>();
        return result!.Data!.Id;
    }

    public async Task SetupProjectRolesAsync(Guid appId)
    {
        // Create 5 permissions
        var permIds = new Dictionary<string, Guid>();
        var perms = new[]
        {
            ("read",           "Read data from project schema"),
            ("write",          "Write data to project schema"),
            ("ddl",            "Execute DDL migrations on project schema"),
            ("manage_members", "Manage project members and roles"),
            ("delete_project", "Deactivate or delete the project")
        };

        foreach (var (permName, permDesc) in perms)
        {
            var resp = await ServiceClient.PostAsJsonAsync(
                $"api/v1/apps/{appId}/permissions",
                new { name = permName, description = permDesc });

            if (resp.StatusCode == HttpStatusCode.Conflict)
            {
                // Permission already exists — fetch list and find it
                var listResp = await ServiceClient.GetFromJsonAsync<SpResponse<IEnumerable<SpPermData>>>(
                    $"api/v1/apps/{appId}/permissions");
                var existing = listResp?.Data?.FirstOrDefault(p => p.Name == permName);
                if (existing != null) permIds[permName] = existing.Id;
                continue;
            }

            resp.EnsureSuccessStatusCode();
            var created = await resp.Content.ReadFromJsonAsync<SpResponse<SpPermData>>();
            permIds[permName] = created!.Data!.Id;
        }

        // Create 3 roles
        var roleIds = new Dictionary<string, Guid>();
        foreach (var roleName in new[] { "owner", "developer", "viewer" })
        {
            var resp = await ServiceClient.PostAsJsonAsync(
                $"api/v1/apps/{appId}/roles",
                new { name = roleName });

            if (resp.StatusCode == HttpStatusCode.Conflict)
            {
                var listResp = await ServiceClient.GetFromJsonAsync<SpResponse<IEnumerable<SpRoleData>>>(
                    $"api/v1/apps/{appId}/roles");
                var existing = listResp?.Data?.FirstOrDefault(r => r.Name == roleName);
                if (existing != null) roleIds[roleName] = existing.Id;
                continue;
            }

            resp.EnsureSuccessStatusCode();
            var created = await resp.Content.ReadFromJsonAsync<SpResponse<SpRoleData>>();
            roleIds[roleName] = created!.Data!.Id;
        }

        // Assign permissions to roles
        var assignments = new[]
        {
            ("owner",     new[] { "read", "write", "ddl", "manage_members", "delete_project" }),
            ("developer", new[] { "read", "write", "ddl" }),
            ("viewer",    new[] { "read" })
        };

        foreach (var (roleName, rolePerms) in assignments)
        {
            if (!roleIds.TryGetValue(roleName, out var roleId)) continue;
            foreach (var perm in rolePerms)
            {
                if (!permIds.TryGetValue(perm, out var permId)) continue;
                var resp = await ServiceClient.PostAsJsonAsync(
                    $"api/v1/apps/{appId}/roles/{roleId}/permissions",
                    new { permissionId = permId });
                if (resp.StatusCode != HttpStatusCode.Conflict)
                    resp.EnsureSuccessStatusCode();
            }
        }
    }

    public async Task GrantRoleAsync(Guid appId, Guid userId, string roleName)
    {
        var roleId = await ResolveRoleIdAsync(appId, roleName);
        var response = await ServiceClient.PostAsJsonAsync(
            $"api/v1/apps/{appId}/users",
            new { userId, roleId });
        response.EnsureSuccessStatusCode();
    }

    public async Task ChangeRoleAsync(Guid appId, Guid userId, string roleName)
    {
        var roleId = await ResolveRoleIdAsync(appId, roleName);
        var response = await ServiceClient.PutAsJsonAsync(
            $"api/v1/apps/{appId}/users/{userId}/role",
            new { roleId });
        response.EnsureSuccessStatusCode();
    }

    public async Task RevokeRoleAsync(Guid appId, Guid userId)
    {
        var response = await ServiceClient.DeleteAsync($"api/v1/apps/{appId}/users/{userId}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<SpUserDto> GetUserAsync(Guid userId)
    {
        var result = await ServiceClient.GetFromJsonAsync<SpResponse<SpUserDto>>(
            $"api/v1/users/{userId}");
        return result?.Data ?? throw new KeyNotFoundException($"User {userId} not found in Security Platform.");
    }

    public async Task<IEnumerable<SpAppAccessDto>> GetUserAppAccessAsync(Guid userId)
    {
        var result = await ServiceClient.GetFromJsonAsync<SpResponse<SpUserDto>>(
            $"api/v1/users/{userId}");
        return result?.Data?.AppAccess ?? [];
    }

    public async Task<IEnumerable<SpAppMemberDto>> GetAppMembersAsync(Guid appId)
    {
        var result = await ServiceClient.GetFromJsonAsync<SpResponse<IEnumerable<SpAppMemberDto>>>(
            $"api/v1/apps/{appId}/users");
        return result?.Data ?? [];
    }

    public async Task<bool> AuthorizeAsync(
        string appSlug,
        string resourceIdentifier,
        string requiredPermission)
    {
        var authHeader = _httpContextAccessor.HttpContext?
            .Request.Headers["Authorization"].ToString();

        var client = _httpClientFactory.CreateClient("SecurityPlatformUser");
        if (!string.IsNullOrWhiteSpace(authHeader))
            client.DefaultRequestHeaders.Authorization =
                AuthenticationHeaderValue.Parse(authHeader);

        var response = await client.PostAsJsonAsync("api/v1/authorize", new
        {
            appSlug,
            resourceIdentifier,
            requiredPermission
        });

        if (!response.IsSuccessStatusCode) return false;
        var result = await response.Content.ReadFromJsonAsync<SpResponse<SpAuthorizeData>>();
        return result?.Data?.Allowed ?? false;
    }

    private async Task<Guid> ResolveRoleIdAsync(Guid appId, string roleName)
    {
        var result = await ServiceClient.GetFromJsonAsync<SpResponse<IEnumerable<SpRoleData>>>(
            $"api/v1/apps/{appId}/roles");
        var role = result?.Data?.FirstOrDefault(r =>
            string.Equals(r.Name, roleName, StringComparison.OrdinalIgnoreCase));
        if (role is null)
            throw new InvalidOperationException($"Role '{roleName}' not found in app {appId}.");
        return role.Id;
    }

    private sealed record SpAppIdData(
        [property: JsonPropertyName("id")] Guid Id);

    // SP response envelope
    private sealed record SpResponse<T>(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("data")] T? Data);

    // Internal SP data shapes (not exposed outside this class)
    private sealed record SpPermData(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name);

    private sealed record SpRoleData(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name);

    private sealed record SpAuthorizeData(
        [property: JsonPropertyName("allowed")] bool Allowed);
}
