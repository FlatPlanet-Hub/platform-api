using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.Application.Services;

public sealed class IamAuthorizationService(
    IAppRepository appRepo,
    IUserAppRoleRepository userAppRoleRepo,
    IRolePermissionRepository rolePermRepo,
    IResourceRepository resourceRepo,
    IAuditService audit) : IIamAuthorizationService
{
    public async Task<AuthorizeResponse> AuthorizeAsync(AuthorizeRequest request)
    {
        var app = await appRepo.GetBySlugAsync(request.AppSlug);
        if (app is null)
            return new AuthorizeResponse { Allowed = false };

        var userRoles = (await userAppRoleRepo.GetByUserAndAppAsync(request.UserId, app.Id))
            .Where(r => r.Status == "active" && (r.ExpiresAt is null || r.ExpiresAt > DateTime.UtcNow))
            .ToList();

        if (userRoles.Count == 0)
        {
            await audit.LogAsync(request.UserId, app.Id, "authorize.denied", "user_app_roles");
            return new AuthorizeResponse { Allowed = false };
        }

        // Collect all permission names across all active roles
        var allPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roleNames = new List<string>();

        foreach (var userRole in userRoles)
        {
            var perms = await rolePermRepo.GetPermissionNamesByRoleIdAsync(userRole.RoleId);
            foreach (var p in perms) allPermissions.Add(p);
        }

        // Check required permission
        if (!string.IsNullOrEmpty(request.RequiredPermission)
            && !allPermissions.Contains(request.RequiredPermission))
        {
            await audit.LogAsync(request.UserId, app.Id, "authorize.denied", "permissions",
                details: new { required = request.RequiredPermission });
            return new AuthorizeResponse { Allowed = false, Permissions = allPermissions.ToList() };
        }

        // Collect resource policies
        var policies = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(request.ResourceIdentifier))
        {
            var resource = await resourceRepo.GetByIdentifierAsync(app.Id, request.ResourceIdentifier);
            // Policies could be fetched here if IResourcePolicyRepository is implemented
            // For now, return empty policies
        }

        await audit.LogAsync(request.UserId, app.Id, "authorize.allowed", "user_app_roles",
            details: new { resource = request.ResourceIdentifier, permission = request.RequiredPermission });

        return new AuthorizeResponse
        {
            Allowed = true,
            Roles = roleNames,
            Permissions = allPermissions.ToList(),
            Policies = policies
        };
    }
}
