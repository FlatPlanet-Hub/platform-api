using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SupabaseProxy.API.Filters;

/// <summary>Requires the authenticated user to have a specific system role (e.g. platform_admin).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireSystemRoleAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _role;

    public RequireSystemRoleAttribute(string role) => _role = role;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) { context.Result = new UnauthorizedResult(); return; }

        var claim = user.FindFirst("system_roles")?.Value;
        if (claim is null || !claim.Contains(_role))
            context.Result = new ForbidResult();
    }
}

/// <summary>
/// Requires the authenticated user to have a specific permission OR be a platform_admin.
/// Permissions are sourced from the JWT 'permissions' claim (resolved at login time).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequirePermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _permission;

    public RequirePermissionAttribute(string permission) => _permission = permission;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) { context.Result = new UnauthorizedResult(); return; }

        // platform_admin bypasses all permission checks
        var systemRolesClaim = user.FindFirst("system_roles")?.Value ?? "[]";
        var systemRoles = JsonSerializer.Deserialize<string[]>(systemRolesClaim) ?? [];
        if (systemRoles.Contains("platform_admin")) return;

        // Check the permissions claim (union of all assigned custom role permissions)
        var permissionsClaim = user.FindFirst("permissions")?.Value ?? "[]";
        var permissions = JsonSerializer.Deserialize<string[]>(permissionsClaim) ?? [];
        if (!permissions.Contains(_permission))
            context.Result = new ForbidResult();
    }
}
