using System.Text;
using FlatPlanet.Platform.Application.DTOs.Auth;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using FlatPlanet.Platform.Application.Common.Helpers;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ClaudeConfigService : IClaudeConfigService
{
    private readonly IProjectRepository _projectRepo;
    private readonly IUserRepository _userRepo;
    private readonly IApiTokenRepository _apiTokenRepo;
    private readonly IJwtService _jwtService;
    private readonly IAuditService _audit;
    private readonly IUserAppRoleRepository _userAppRoleRepo;
    private readonly IRolePermissionRepository _rolePermRepo;

    public ClaudeConfigService(
        IProjectRepository projectRepo,
        IUserRepository userRepo,
        IApiTokenRepository apiTokenRepo,
        IJwtService jwtService,
        IAuditService audit,
        IUserAppRoleRepository userAppRoleRepo,
        IRolePermissionRepository rolePermRepo)
    {
        _projectRepo = projectRepo;
        _userRepo = userRepo;
        _apiTokenRepo = apiTokenRepo;
        _jwtService = jwtService;
        _audit = audit;
        _userAppRoleRepo = userAppRoleRepo;
        _rolePermRepo = rolePermRepo;
    }

    public async Task<ClaudeConfigResponse> GenerateAsync(Guid userId, Guid projectId, string baseUrl)
    {
        var (user, project, permissions) = await GetContextAsync(userId, projectId);

        var rawToken = _jwtService.GenerateApiToken(
            user, null, project.SchemaName, project.SchemaName,
            permissions, 30, out var expiresAt);

        var tokenHash = TokenHasher.Hash(rawToken);

        var stored = await _apiTokenRepo.CreateAsync(new ApiToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AppId = project.AppId,   // Issue 14: scope token to the project's app
            Name = $"Claude token for {project.Name}",
            TokenHash = tokenHash,
            Permissions = permissions,
            ExpiresAt = expiresAt,
            Revoked = false,
            CreatedAt = DateTime.UtcNow
        });

        await _audit.LogAsync(userId, project.AppId, "claude_config.generated", "api_tokens",
            new { tokenId = stored.Id });

        return new ClaudeConfigResponse
        {
            Content = RenderTemplate(project, rawToken, expiresAt, baseUrl),
            TokenId = stored.Id,
            ExpiresAt = expiresAt
        };
    }

    public async Task<ClaudeConfigResponse> RegenerateAsync(Guid userId, Guid projectId, string baseUrl)
    {
        // Issue 14: scope revocation to this project's app only
        var project = await _projectRepo.GetByIdAsync(projectId)
            ?? throw new KeyNotFoundException($"Project {projectId} not found.");

        var tokens = await _apiTokenRepo.GetActiveByUserIdAsync(userId);
        foreach (var t in tokens.Where(t => t.AppId == project.AppId))
            await _apiTokenRepo.RevokeAsync(t.Id, "regenerated");

        await _audit.LogAsync(userId, project.AppId, "claude_config.revoked_all", "api_tokens");
        return await GenerateAsync(userId, projectId, baseUrl);
    }

    public async Task RevokeAsync(Guid userId, Guid projectId)
    {
        var project = await _projectRepo.GetByIdAsync(projectId)
            ?? throw new KeyNotFoundException($"Project {projectId} not found.");

        var tokens = await _apiTokenRepo.GetActiveByUserIdAsync(userId);
        foreach (var t in tokens.Where(t => t.AppId == project.AppId))
            await _apiTokenRepo.RevokeAsync(t.Id, "revoked");

        await _audit.LogAsync(userId, project.AppId, "claude_config.revoked", "api_tokens");
    }

    public async Task<IEnumerable<ClaudeTokenSummaryDto>> ListActiveTokensAsync(Guid userId)
    {
        var tokens = await _apiTokenRepo.GetActiveByUserIdAsync(userId);
        return tokens.Select(t => new ClaudeTokenSummaryDto
        {
            TokenId = t.Id,
            ProjectId = Guid.Empty,
            ProjectName = t.Name,
            ExpiresAt = t.ExpiresAt,
            CreatedAt = t.CreatedAt
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Issue 15: replaced project_members/project_roles with user_app_roles/role_permissions
    private async Task<(User user, Project project, string[] permissions)> GetContextAsync(Guid userId, Guid projectId)
    {
        var project = await _projectRepo.GetByIdAsync(projectId)
            ?? throw new KeyNotFoundException($"Project {projectId} not found.");

        if (project.AppId is null)
            throw new InvalidOperationException($"Project {projectId} is not linked to an IAM app.");

        string[] permissions;
        if (project.OwnerId == userId)
        {
            permissions = ["read", "write", "ddl", "manage_members"];
        }
        else
        {
            var userRoles = (await _userAppRoleRepo.GetByUserAndAppAsync(userId, project.AppId.Value))
                .Where(r => r.Status == "active" && (r.ExpiresAt is null || r.ExpiresAt > DateTime.UtcNow))
                .ToList();

            if (userRoles.Count == 0)
                throw new UnauthorizedAccessException("You do not have access to this project.");

            var allPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ur in userRoles)
            {
                var perms = await _rolePermRepo.GetPermissionNamesByRoleIdAsync(ur.RoleId);
                foreach (var p in perms) allPermissions.Add(p);
            }
            permissions = allPermissions.ToArray();
        }

        var user = await _userRepo.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        return (user, project, permissions);
    }

    // Issue 13: corrected API paths — project context comes from JWT, not the URL
    private static string RenderTemplate(Project project, string token, DateTime expiresAt, string baseUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Project Context");
        sb.AppendLine();
        sb.AppendLine("## Project");
        sb.AppendLine($"- **Name**: {project.Name}");
        sb.AppendLine($"- **Description**: {project.Description ?? string.Empty}");
        sb.AppendLine($"- **Project ID**: {project.Id}");
        sb.AppendLine($"- **Schema**: {project.SchemaName}");
        sb.AppendLine();
        sb.AppendLine("## Platform API");
        sb.AppendLine();
        sb.AppendLine($"Base URL: {baseUrl}");
        sb.AppendLine($"Token: {token}");
        sb.AppendLine($"Token Expires: {expiresAt:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("All API requests require this header:");
        sb.AppendLine($"Authorization: Bearer {token}");
        sb.AppendLine();
        sb.AppendLine("### Read Schema (ALWAYS DO THIS FIRST)");
        sb.AppendLine($"GET {baseUrl}/api/schema/full");
        sb.AppendLine();
        sb.AppendLine("### Create Table");
        sb.AppendLine($"POST {baseUrl}/api/migration/create-table");
        sb.AppendLine();
        sb.AppendLine("### Read Query");
        sb.AppendLine($"POST {baseUrl}/api/query/read");
        sb.AppendLine();
        sb.AppendLine("### Write Query");
        sb.AppendLine($"POST {baseUrl}/api/query/write");
        sb.AppendLine();
        sb.AppendLine("## Rules");
        sb.AppendLine("1. ALWAYS read the schema first before writing any database code");
        sb.AppendLine("2. ALWAYS use parameterized queries — NEVER concatenate values into SQL");
        sb.AppendLine("3. Use migration endpoints for CREATE/ALTER/DROP — never raw DDL in query endpoints");
        sb.AppendLine("4. All database access must go through the API — NEVER connect to the database directly");
        sb.AppendLine();
        sb.AppendLine("## IMPORTANT");
        sb.AppendLine("- This file is LOCAL ONLY — do not commit or push this file");
        sb.AppendLine("- If the token has expired, regenerate it from the app");
        return sb.ToString();
    }
}
