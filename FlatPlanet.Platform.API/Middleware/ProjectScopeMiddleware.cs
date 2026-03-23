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
        if (context.Request.Path.StartsWithSegments("/api/token") ||
            context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        const string AppTokenType = "app";

        var tokenType = context.User.FindFirst("token_type")?.Value;

        // App JWT tokens (frontend use) carry per-app claims in an apps[] array — no single schema
        if (tokenType == AppTokenType)
        {
            await _next(context);
            return;
        }

        // API tokens (Claude Code / service) carry schema + permissions in flat claims
        var userId = context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                     ?? context.User.FindFirst("sub")?.Value;
        var projectId = context.User.FindFirst("project_id")?.Value;
        var schema = context.User.FindFirst("schema")?.Value;
        var permissions = context.User.FindFirst("permissions")?.Value;

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
            ProjectId = projectId ?? string.Empty,
            Schema = schema,
            Permissions = permissions?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          ?? []
        };

        context.Items[ClaimsKey] = claims;

        await _next(context);
    }
}
