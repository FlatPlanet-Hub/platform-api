using FlatPlanet.Platform.Application.DTOs;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IJwtService
{
    // Feature 1 — scoped proxy token (direct DB access)
    string GenerateToken(GenerateTokenRequest request);

    // Feature 6 — long-lived API token (Claude Code, CI/CD, integrations)
    string GenerateApiToken(Guid userId, string userName, string userEmail, Guid? appId, string appSlug, string? schema, string[] permissions, int expiryDays, out DateTime expiresAt);
}
