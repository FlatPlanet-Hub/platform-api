using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Auth;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using FlatPlanet.Platform.Infrastructure.Common.Helpers;
using FlatPlanet.Platform.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/auth")]
public sealed class AuthController : ApiControllerBase
{
    private readonly IGitHubOAuthService _gitHub;
    private readonly IUserService _userService;
    private readonly IJwtService _jwtService;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IAuditService _audit;
    private readonly IClaudeConfigService _claudeConfigService;
    private readonly JwtSettings _jwtSettings;

    public AuthController(
        IGitHubOAuthService gitHub,
        IUserService userService,
        IJwtService jwtService,
        IRefreshTokenRepository refreshTokenRepo,
        IAuditService audit,
        IClaudeConfigService claudeConfigService,
        IOptions<JwtSettings> jwtSettings)
    {
        _gitHub = gitHub;
        _userService = userService;
        _jwtService = jwtService;
        _refreshTokenRepo = refreshTokenRepo;
        _audit = audit;
        _claudeConfigService = claudeConfigService;
        _jwtSettings = jwtSettings.Value;
    }

    [HttpGet("github")]
    public IActionResult GitHub()
    {
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Response.Cookies.Append("oauth_state", state, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(5)
        });

        var url = _gitHub.BuildAuthorizationUrl(state);
        return Redirect(url);
    }

    [HttpGet("github/callback")]
    public async Task<IActionResult> GitHubCallback([FromQuery] string code, [FromQuery] string state)
    {
        var storedState = Request.Cookies["oauth_state"];
        if (string.IsNullOrWhiteSpace(storedState) || storedState != state)
            return BadRequest(ApiResponse<object>.Fail("Invalid OAuth state. Possible CSRF attack."));

        Response.Cookies.Delete("oauth_state");

        var accessToken = await _gitHub.ExchangeCodeForTokenAsync(code);
        var profile = await _gitHub.GetUserProfileAsync(accessToken);
        var user = await _userService.UpsertFromGitHubAsync(profile);

        var (appToken, refreshToken) = await IssueTokenPairAsync(user);

        await _audit.LogAsync(user.Id, null, "login_success",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        var frontendUrl = $"#access_token={Uri.EscapeDataString(appToken)}&refresh_token={Uri.EscapeDataString(refreshToken)}";
        return Redirect(frontendUrl);
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Refresh([FromBody] RefreshTokenRequest request)
    {
        var hash = EncryptionHelper.HashToken(request.RefreshToken);
        var stored = await _refreshTokenRepo.GetByHashAsync(hash);

        if (stored is null || stored.ExpiresAt < DateTime.UtcNow)
            return Unauthorized(ApiResponse<AuthResponse>.Fail("Invalid or expired refresh token."));

        await _refreshTokenRepo.RevokeAsync(stored.Id);

        var user = await _userService.GetProfileAsync(stored.UserId);
        var domainUser = ProfileToUser(user);

        var (appToken, newRefreshToken) = await IssueTokenPairAsync(domainUser);
        return Ok(ApiResponse<AuthResponse>.Ok(new AuthResponse
        {
            AccessToken = appToken,
            RefreshToken = newRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes)
        }));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object?>>> Logout([FromBody] RefreshTokenRequest request)
    {
        var hash = EncryptionHelper.HashToken(request.RefreshToken);
        var stored = await _refreshTokenRepo.GetByHashAsync(hash);
        if (stored is not null)
            await _refreshTokenRepo.RevokeAsync(stored.Id);

        await _audit.LogAsync(GetUserId(), null, "logout");
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserProfileResponse>>> Me()
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();
        var profile = await _userService.GetProfileAsync(userId.Value);
        return Ok(ApiResponse<UserProfileResponse>.Ok(profile));
    }

    // ── Legacy Claude token endpoints (kept for backward compat — use /api/auth/api-tokens) ──

    [HttpGet("claude-tokens")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<IEnumerable<ClaudeTokenSummaryDto>>>> ListClaudeTokens()
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();
        var tokens = await _claudeConfigService.ListActiveTokensAsync(userId.Value);
        return Ok(ApiResponse<IEnumerable<ClaudeTokenSummaryDto>>.Ok(tokens));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(string appToken, string refreshToken)> IssueTokenPairAsync(User user)
    {
        // Feature 6: build IAM app claims from user_app_roles
        var appClaims = await _userService.GetIamAppClaimsAsync(user.Id);
        var appToken = _jwtService.GenerateAppToken(user, appClaims);

        var rawRefresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        await _refreshTokenRepo.CreateAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = EncryptionHelper.HashToken(rawRefresh),
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
            Revoked = false,
            CreatedAt = DateTime.UtcNow
        });

        return (appToken, rawRefresh);
    }

    private static User ProfileToUser(UserProfileResponse profile) => new()
    {
        Id = profile.Id,
        GitHubUsername = profile.GitHubUsername ?? string.Empty,
        FullName = $"{profile.FirstName} {profile.LastName}".Trim(),
        Email = profile.Email ?? string.Empty,
        AvatarUrl = profile.AvatarUrl
    };
}
