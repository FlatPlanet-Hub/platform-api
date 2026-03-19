using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SupabaseProxy.API.Middleware;
using SupabaseProxy.Domain.Entities;

namespace SupabaseProxy.API.Filters;

/// <summary>Requires the authenticated user to have a specific system role claim.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireSystemRoleAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _role;

    public RequireSystemRoleAttribute(string role) => _role = role;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var systemRolesClaim = user.FindFirst("system_roles")?.Value;
        if (systemRolesClaim is null || !systemRolesClaim.Contains(_role))
        {
            context.Result = new ForbidResult();
        }
    }
}
