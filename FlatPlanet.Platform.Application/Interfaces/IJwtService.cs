using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IJwtService
{
    // Feature 1 — scoped proxy token (direct DB access)
    string GenerateToken(GenerateTokenRequest request);

    // Feature 6 — short-lived app JWT with apps[] claims
    string GenerateAppToken(User user, IEnumerable<IamAppClaims> apps);

    // Feature 6 — long-lived API token (Claude Code, CI/CD, integrations)
    string GenerateApiToken(User user, Guid? appId, string appSlug, string? schema, string[] permissions, int expiryDays, out DateTime expiresAt);
}
