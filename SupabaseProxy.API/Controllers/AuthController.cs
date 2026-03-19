using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupabaseProxy.Application.DTOs;
using SupabaseProxy.Application.DTOs.Auth;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;
using SupabaseProxy.Infrastructure.Common.Helpers;
using SupabaseProxy.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace SupabaseProxy.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IGitHubOAuthService _gitHub;
    private readonly IUserService _userService;
    private readonly IProjectService _projectService;
    private readonly IJwtService _jwtService;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IClaudeTokenRepository _claudeTokenRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly IAuditService _audit;
    private readonly JwtSettings _jwtSettings;

    public AuthController(
        IGitHubOAuthService gitHub,
        IUserService userService,
        IProjectService projectService,
        IJwtService jwtService,
        IRefreshTokenRepository refreshTokenRepo,
        IClaudeTokenRepository claudeTokenRepo,
        IProjectRepository projectRepo,
        IAuditService audit,
        IOptions<JwtSettings> jwtSettings)
    {
        _gitHub = gitHub;
        _userService = userService;
        _projectService = projectService;
        _jwtService = jwtService;
        _refreshTokenRepo = refreshTokenRepo;
        _claudeTokenRepo = claudeTokenRepo;
        _projectRepo = projectRepo;
        _audit = audit;
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

        await _audit.LogAsync(user.Id, null, "auth.login", "auth",
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

        var user = await GetCurrentUserFromIdAsync(stored.UserId);
        if (user is null) return Unauthorized(ApiResponse<AuthResponse>.Fail("User not found."));

        var (appToken, newRefreshToken) = await IssueTokenPairAsync(user);
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

    [HttpPost("claude-token")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<ClaudeTokenResponse>>> GenerateClaudeToken([FromBody] ClaudeTokenRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var project = await _projectRepo.GetByIdAsync(request.ProjectId);
        if (project is null) return NotFound(ApiResponse<object>.Fail("Project not found."));

        var member = await _projectRepo.GetMemberAsync(request.ProjectId, userId.Value);
        if (member is null) return Forbid();

        var role = await _projectRepo.GetRoleAsync(request.ProjectId, member.ProjectRoleId);
        var permissions = role?.Permissions ?? [];

        var user = await GetCurrentUserFromIdAsync(userId.Value);
        if (user is null) return Unauthorized();

        var rawToken = _jwtService.GenerateClaudeToken(user, project, permissions, out var expiresAt);
        var tokenHash = EncryptionHelper.HashToken(rawToken);

        var claudeToken = await _claudeTokenRepo.CreateAsync(new ClaudeToken
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            ProjectId = request.ProjectId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            Revoked = false,
            CreatedAt = DateTime.UtcNow
        });

        await _audit.LogAsync(userId, request.ProjectId, "claude_token.created", "claude_tokens");

        var response = new ClaudeTokenResponse
        {
            TokenId = claudeToken.Id,
            Token = rawToken,
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
                            ["API_URL"] = $"{Request.Scheme}://{Request.Host}",
                            ["API_TOKEN"] = rawToken
                        }
                    }
                }
            }
        };

        return Ok(ApiResponse<ClaudeTokenResponse>.Ok(response));
    }

    [HttpGet("claude-tokens")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<IEnumerable<object>>>> ListClaudeTokens()
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var tokens = await _claudeTokenRepo.GetActiveByUserIdAsync(userId.Value);
        var result = tokens.Select(t => new { t.Id, t.ProjectId, t.ExpiresAt, t.CreatedAt });
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpDelete("claude-tokens/{tokenId:guid}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object?>>> RevokeClaudeToken(Guid tokenId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var token = await _claudeTokenRepo.GetByIdAsync(tokenId);
        if (token is null || token.UserId != userId.Value)
            return NotFound(ApiResponse<object>.Fail("Token not found."));

        await _claudeTokenRepo.RevokeAsync(tokenId);
        await _audit.LogAsync(userId, token.ProjectId, "claude_token.revoked", "claude_tokens", new { tokenId });
        return Ok(ApiResponse<object?>.Ok(null));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(string appToken, string refreshToken)> IssueTokenPairAsync(User user)
    {
        var projects = await _userService.GetUserProjectsForTokenAsync(user.Id);
        var userRoleNames = (await GetUserSystemRoleNamesAsync(user.Id)).ToList();
        var effectivePermissions = await _userService.GetEffectivePermissionsAsync(user.Id, userRoleNames);

        var appToken = _jwtService.GenerateAppToken(user, projects, userRoleNames, effectivePermissions);

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

    private async Task<IEnumerable<string>> GetUserSystemRoleNamesAsync(Guid userId)
    {
        var profile = await _userService.GetProfileAsync(userId);
        return profile.SystemRoles;
    }

    private async Task<User?> GetCurrentUserFromIdAsync(Guid userId)
    {
        var profile = await _userService.GetProfileAsync(userId);
        return new User
        {
            Id = profile.Id,
            GitHubUsername = profile.GitHubUsername,
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Email = profile.Email,
            AvatarUrl = profile.AvatarUrl
        };
    }

    private Guid? GetUserId()
    {
        var sub = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
