using System.Security.Cryptography;
using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using FlatPlanet.Platform.Application.Common.Helpers;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ApiTokenService(
    IApiTokenRepository tokenRepo,
    IAppRepository appRepo,
    IJwtService jwtService,
    IUserRepository userRepo) : IApiTokenService
{
    public async Task<ApiTokenResponse> CreateAsync(Guid userId, CreateApiTokenRequest request, string apiBaseUrl)
    {
        var user = await userRepo.GetByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        App? app = null;
        string appSlug = "platform";
        string? schema = null;

        if (request.AppId.HasValue)
        {
            app = await appRepo.GetByIdAsync(request.AppId.Value)
                ?? throw new InvalidOperationException("App not found.");
            appSlug = app.Slug;
            schema = app.SchemaName;
        }

        var rawToken = jwtService.GenerateApiToken(
            user, app?.Id, appSlug, schema,
            request.Permissions, request.ExpiryDays, out var expiresAt);

        var tokenHash = TokenHasher.Hash(rawToken);

        var apiToken = await tokenRepo.CreateAsync(new ApiToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AppId = app?.Id,
            Name = request.Name,
            TokenHash = tokenHash,
            Permissions = request.Permissions,
            ExpiresAt = expiresAt,
            Revoked = false,
            CreatedAt = DateTime.UtcNow
        });

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

    public async Task RevokeAsync(Guid tokenId, Guid userId)
    {
        var token = await tokenRepo.GetByIdAsync(tokenId)
            ?? throw new InvalidOperationException("Token not found.");

        if (token.UserId != userId)
            throw new UnauthorizedAccessException("Token does not belong to the current user.");

        await tokenRepo.RevokeAsync(tokenId, "user_revoke");
    }
}
