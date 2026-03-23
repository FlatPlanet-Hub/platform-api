using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Auth;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IJwtService
{
    // Feature 1 — scoped proxy token
    string GenerateToken(GenerateTokenRequest request);

    // Feature 2/3 — app token (frontend); permissions = union of all effective permissions
    string GenerateAppToken(User user, IEnumerable<UserProjectSummaryDto> projects, IEnumerable<string> systemRoles, IEnumerable<string> effectivePermissions);

    // Feature 2 — long-lived Claude Desktop token
    string GenerateClaudeToken(User user, Project project, string[] permissions, out DateTime expiresAt);
}
