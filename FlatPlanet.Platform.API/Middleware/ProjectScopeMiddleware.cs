using FlatPlanet.Platform.Application.Common.Helpers;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.API.Middleware;

public sealed class ProjectScopeMiddleware
{
    public const string ClaimsKey = "ProjectClaims";

    private readonly RequestDelegate _next;
    private readonly ILogger<ProjectScopeMiddleware> _logger;

    public ProjectScopeMiddleware(RequestDelegate next, ILogger<ProjectScopeMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/swagger") ||
            context.Request.Path.StartsWithSegments("/scalar") ||
            context.Request.Path.StartsWithSegments("/api/auth"))
        {
            await _next(context);
            return;
        }

        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        var tokenType = context.User.FindFirst("token_type")?.Value;
        var hasProjectRoute = context.Request.RouteValues.ContainsKey("projectId");

        // Only extract project-scope claims when BOTH:
        // 1. The route has a {projectId} segment (schema/migration/query endpoints)
        // 2. The token is an API token (Claude Code / CI/CD)
        // All other requests — including Security Platform JWTs — pass through unconditionally.
        if (tokenType != "api_token" || !hasProjectRoute)
        {
            await _next(context);
            return;
        }

        // API tokens (Claude Code / service) carry schema + permissions in flat claims
        var userId = context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                     ?? context.User.FindFirst("sub")?.Value;
        var tokenProjectId = context.User.FindFirst("project_id")?.Value;
        var routeProjectId = context.Request.RouteValues["projectId"]?.ToString();
        var schema = context.User.FindFirst("schema")?.Value;
        var permissions = context.User.FindFirst("permissions")?.Value;

        // Validate route projectId matches token project_id claim
        if (!string.IsNullOrWhiteSpace(tokenProjectId) &&
            !string.Equals(tokenProjectId, routeProjectId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Route projectId {RouteId} does not match token project_id {TokenId} for user {UserId}",
                routeProjectId, tokenProjectId, userId);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Token is not scoped to this project." });
            return;
        }

        if (string.IsNullOrWhiteSpace(schema) || !SqlValidationHelper.IsValidSchemaName(schema))
        {
            _logger.LogWarning("Invalid or missing schema claim for user {UserId}", userId);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid schema in token." });
            return;
        }

        var claims = new ProjectClaims
        {
            UserId = userId ?? string.Empty,
            ProjectId = routeProjectId ?? tokenProjectId ?? string.Empty,
            Schema = schema,
            Permissions = permissions?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          ?? []
        };

        context.Items[ClaimsKey] = claims;

        await _next(context);
    }
}
