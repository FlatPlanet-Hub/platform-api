using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SupabaseProxy.Application.DTOs;
using SupabaseProxy.Application.DTOs.Auth;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;
using SupabaseProxy.Domain.Enums;
using SupabaseProxy.Infrastructure.Configuration;

namespace SupabaseProxy.Infrastructure.ExternalServices;

public sealed class JwtService : IJwtService
{
    private readonly JwtSettings _settings;

    public JwtService(IOptions<JwtSettings> settings) => _settings = settings.Value;

    private SymmetricSecurityKey SigningKey =>
        new(Encoding.UTF8.GetBytes(_settings.SecretKey));

    // Feature 1 — direct scoped proxy token
    public string GenerateToken(GenerateTokenRequest request)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, request.UserId),
            new Claim("project_id", request.ProjectId),
            new Claim("schema", request.Schema),
            new Claim("permissions", request.Permissions),
            new Claim("token_type", TokenType.Claude),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        return BuildToken(claims, DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes));
    }

    // Feature 2 — short-lived app token for the frontend
    public string GenerateAppToken(User user, IEnumerable<UserProjectSummaryDto> projects, IEnumerable<string> systemRoles)
    {
        var projectsJson = JsonSerializer.Serialize(projects.Select(p => new
        {
            project_id = p.ProjectId,
            schema = p.Schema,
            project_role = p.ProjectRole,
            permissions = p.Permissions
        }));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("github_id", user.GitHubId.ToString()),
            new Claim("github_username", user.GitHubUsername),
            new Claim("display_name", user.DisplayName),
            new Claim("system_roles", JsonSerializer.Serialize(systemRoles)),
            new Claim("projects", projectsJson),
            new Claim("token_type", TokenType.App),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        return BuildToken(claims, DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpiryMinutes));
    }

    // Feature 2 — long-lived Claude Desktop token (single project scope)
    public string GenerateClaudeToken(User user, Project project, string[] permissions, out DateTime expiresAt)
    {
        expiresAt = DateTime.UtcNow.AddDays(_settings.ClaudeTokenExpiryDays);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("github_id", user.GitHubId.ToString()),
            new Claim("project_id", project.Id.ToString()),
            new Claim("schema", project.SchemaName),
            new Claim("permissions", string.Join(",", permissions)),
            new Claim("token_type", TokenType.Claude),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

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
