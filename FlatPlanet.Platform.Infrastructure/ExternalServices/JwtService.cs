using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Infrastructure.Configuration;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class JwtService : IJwtService
{
    private readonly JwtSettings _settings;

    public JwtService(IOptions<JwtSettings> settings) => _settings = settings.Value;

    private SymmetricSecurityKey SigningKey =>
        new(Encoding.UTF8.GetBytes(_settings.SecretKey));

    // Long-lived API token (Claude Code, CI/CD, integrations)
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
