using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using FlatPlanet.Platform.Infrastructure.Configuration;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class JwtService : IJwtService
{
    private readonly JwtSettings _settings;

    public JwtService(IOptions<JwtSettings> settings) => _settings = settings.Value;

    private SymmetricSecurityKey SigningKey =>
        new(Encoding.UTF8.GetBytes(_settings.SecretKey));

    // Feature 1 — direct scoped proxy token (schema + permissions in flat claims)
    public string GenerateToken(GenerateTokenRequest request)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, request.UserId),
            new Claim("project_id", request.ProjectId),
            new Claim("schema", request.Schema),
            new Claim("permissions", request.Permissions),
            new Claim("token_type", "api_token"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        return BuildToken(claims, DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes));
    }

    // Feature 6 — short-lived app JWT with apps[] array claims + system_roles
    public string GenerateAppToken(User user, IEnumerable<IamAppClaims> apps, IEnumerable<string> systemRoles)
    {
        var appsJson = JsonSerializer.Serialize(apps.Select(a => new
        {
            app_id = a.AppId,
            app_slug = a.AppSlug,
            schema = a.Schema,
            roles = a.Roles,
            permissions = a.Permissions
        }));

        var systemRolesJson = JsonSerializer.Serialize(systemRoles.ToArray());

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("email", user.Email),
            new("full_name", user.FullName),
            new("apps", appsJson),
            new("system_roles", systemRolesJson),
            new("token_type", "app"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (user.CompanyId.HasValue)
            claims.Add(new Claim("company_id", user.CompanyId.Value.ToString()));

        if (!string.IsNullOrEmpty(user.GitHubUsername))
            claims.Add(new Claim("github_username", user.GitHubUsername));

        return BuildToken(claims, DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpiryMinutes));
    }

    // Feature 6 — long-lived API token (Claude Code, CI/CD, integrations)
    public string GenerateApiToken(Guid userId, string userName, string userEmail, Guid? appId, string appSlug, string? schema, string[] permissions, int expiryDays, out DateTime expiresAt)
    {
        expiresAt = DateTime.UtcNow.AddDays(expiryDays);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("name", userName),
            new("email", userEmail),
            new("app_slug", appSlug),
            new("permissions", string.Join(",", permissions)),
            new("token_type", "api_token"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (appId.HasValue)
            claims.Add(new Claim("app_id", appId.Value.ToString()));

        if (!string.IsNullOrEmpty(schema))
            claims.Add(new Claim("schema", schema));

        return BuildToken(claims, expiresAt);
    }

    private string BuildToken(IEnumerable<Claim> claims, DateTime expires)
    {
        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
