using FlatPlanet.Platform.Application.Common;
using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using FlatPlanet.Platform.Application.Common.Helpers;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ApiTokenService(
    IApiTokenRepository tokenRepo,
    IJwtService jwtService,
    IAuditLogRepository auditLog) : IApiTokenService
{
    public async Task<ApiTokenResponse> CreateAsync(Guid userId, string userName, string userEmail, CreateApiTokenRequest request, string apiBaseUrl)
    {
        var rawToken = jwtService.GenerateApiToken(
            userId, userName, userEmail,
            request.AppId, "platform", null,
            request.Permissions, request.ExpiryDays, out var expiresAt);

        var tokenHash = TokenHasher.Hash(rawToken);

        var apiToken = await tokenRepo.CreateAsync(new ApiToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AppId = request.AppId,
            Name = request.Name,
            TokenHash = tokenHash,
            Permissions = request.Permissions,
            ExpiresAt = expiresAt,
            Revoked = false,
            CreatedAt = DateTime.UtcNow
        });

        await auditLog.LogAsync(userId, userEmail, AuditAction.TokenCreate,
            "api_token", apiToken.Id, new { tokenId = apiToken.Id, name = request.Name },
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
