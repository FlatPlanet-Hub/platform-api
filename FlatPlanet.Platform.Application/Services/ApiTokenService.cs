using FlatPlanet.Platform.Application.Common;
using FlatPlanet.Platform.Application.Common.Helpers;
using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ApiTokenService(
    IApiTokenRepository tokenRepo,
    IJwtService jwtService,
    IAuditLogRepository auditLog,
    IProjectRepository projectRepo,
    ISecurityPlatformService securityPlatform,
    ILogger<ApiTokenService> logger) : IApiTokenService
{
    private const int MinExpiryDays = 1;
    private const int MaxExpiryDays = 365;

    public async Task<ApiTokenResponse> CreateAsync(Guid userId, string userName, string userEmail, CreateApiTokenRequest request, string apiBaseUrl)
    {
        var expiryDays = Math.Clamp(request.ExpiryDays, MinExpiryDays, MaxExpiryDays);

        // Resolve project (optional) — gives us the right app_slug + schema claim scoping.
        Domain.Entities.Project? project = null;
        if (request.ProjectId is Guid pid)
        {
            project = await projectRepo.GetByIdAsync(pid);
            if (project is null)
                throw new KeyNotFoundException($"Project {pid} not found.");
        }

        var appId = request.AppId ?? project?.AppId;

        // Permission whitelist — log-only warning phase.
        // If the caller is requesting permissions they don't hold on the target app,
        // log it. Hard enforcement will be flipped on in a follow-up PR.
        if (appId is Guid targetAppId && request.Permissions.Length > 0)
        {
            var appAccess = await securityPlatform.GetUserAppAccessAsync(userId);
            var caller = appAccess.FirstOrDefault(a => a.AppId == targetAppId);
            var held = caller?.Permissions ?? [];
            var missing = request.Permissions
                .Where(p => !held.Contains(p, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            if (missing.Length > 0)
            {
                logger.LogWarning(
                    "Token mint request from user {UserId} for app {AppId} requested permissions [{Missing}] not held by caller. Held: [{Held}]. (Log-only phase — not blocking.)",
                    userId, targetAppId, string.Join(",", missing), string.Join(",", held));
            }
        }

        var appSlug = project?.AppSlug ?? project?.SchemaName ?? "platform";
        var schema = project?.SchemaName;

        var rawToken = jwtService.GenerateApiToken(
            userId, userName, userEmail,
            appId, appSlug, schema,
            request.Permissions, expiryDays, out var expiresAt);

        var tokenHash = TokenHasher.Hash(rawToken);

        var apiToken = await tokenRepo.CreateAsync(new ApiToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AppId = appId,
            Name = request.Name,
            TokenHash = tokenHash,
            Permissions = request.Permissions,
            ExpiresAt = expiresAt,
            Revoked = false,
            CreatedAt = DateTime.UtcNow
        });

        await auditLog.LogAsync(userId, userEmail, AuditAction.TokenCreate,
            "api_token", apiToken.Id, new { tokenId = apiToken.Id, name = request.Name, projectId = request.ProjectId, appId, permissions = request.Permissions },
            ipAddress: null);

        return new ApiTokenResponse
        {
            TokenId = apiToken.Id,
            Token = rawToken,
            Name = request.Name,
            Permissions = request.Permissions,
            ExpiresAt = expiresAt,
            McpConfig = new McpConfigDto
            {
                McpServers = new Dictionary<string, McpServerDto>
                {
                    ["flatplanet"] = new McpServerDto
                    {
                        Command = "npx",
                        Args = ["-y", "flatplanet-mcp"],
                        Env = new Dictionary<string, string>
                        {
                            ["API_URL"] = apiBaseUrl,
                            ["API_TOKEN"] = rawToken
                        }
                    }
                }
            }
        };
    }

    public async Task<IEnumerable<ApiTokenSummaryDto>> ListActiveAsync(Guid userId)
    {
        var tokens = await tokenRepo.GetActiveByUserIdAsync(userId);
        return tokens.Select(t => new ApiTokenSummaryDto
        {
            Id = t.Id,
            Name = t.Name,
            AppId = t.AppId,
            Permissions = t.Permissions,
            ExpiresAt = t.ExpiresAt,
            LastUsedAt = t.LastUsedAt,
            CreatedAt = t.CreatedAt
        });
    }

    public async Task RevokeAsync(Guid tokenId, Guid userId, string actorEmail)
    {
        var token = await tokenRepo.GetByIdAsync(tokenId)
            ?? throw new InvalidOperationException("Token not found.");

        if (token.UserId != userId)
            throw new UnauthorizedAccessException("Token does not belong to the current user.");

        await tokenRepo.RevokeAsync(tokenId, "user_revoke");

        await auditLog.LogAsync(userId, actorEmail, AuditAction.TokenRevoke,
            "api_token", tokenId, new { tokenId },
            ipAddress: null);
    }
}
