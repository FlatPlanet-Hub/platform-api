using SupabaseProxy.Application.DTOs;
using SupabaseProxy.Application.DTOs.Auth;
using SupabaseProxy.Domain.Entities;

namespace SupabaseProxy.Application.Interfaces;

public interface IJwtService
{
    // Feature 1 — scoped proxy token
    string GenerateToken(GenerateTokenRequest request);

    // Feature 2 — app token (frontend)
    string GenerateAppToken(User user, IEnumerable<UserProjectSummaryDto> projects, IEnumerable<string> systemRoles);

    // Feature 2 — long-lived Claude Desktop token
    string GenerateClaudeToken(User user, Project project, string[] permissions, out DateTime expiresAt);
}
