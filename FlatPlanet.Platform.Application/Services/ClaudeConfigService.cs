using System.Text;
using FlatPlanet.Platform.Application.DTOs.Auth;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using FlatPlanet.Platform.Application.Common.Helpers;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ClaudeConfigService : IClaudeConfigService
{
    private readonly IProjectRepository _projectRepo;
    private readonly IProjectMemberRepository _memberRepo;
    private readonly IProjectRoleRepository _roleRepo;
    private readonly IUserRepository _userRepo;
    private readonly IApiTokenRepository _apiTokenRepo;
    private readonly IJwtService _jwtService;
    private readonly IAuditService _audit;

    public ClaudeConfigService(
        IProjectRepository projectRepo,
        IProjectMemberRepository memberRepo,
        IProjectRoleRepository roleRepo,
        IUserRepository userRepo,
        IApiTokenRepository apiTokenRepo,
        IJwtService jwtService,
        IAuditService audit)
    {
        _projectRepo = projectRepo;
        _memberRepo = memberRepo;
        _roleRepo = roleRepo;
        _userRepo = userRepo;
        _apiTokenRepo = apiTokenRepo;
        _jwtService = jwtService;
        _audit = audit;
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
            AppId = null,
            Name = $"Claude token for {project.Name}",
            TokenHash = tokenHash,
            Permissions = permissions,
            ExpiresAt = expiresAt,
            Revoked = false,
            CreatedAt = DateTime.UtcNow
        });

        await _audit.LogAsync(userId, null, "claude_config.generated", "api_tokens",
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
        // Revoke all active tokens for this user/project combo
        var tokens = await _apiTokenRepo.GetActiveByUserIdAsync(userId);
        foreach (var t in tokens.Where(t => !t.AppId.HasValue))
            await _apiTokenRepo.RevokeAsync(t.Id, "regenerated");

        await _audit.LogAsync(userId, null, "claude_config.revoked_all", "api_tokens");
        return await GenerateAsync(userId, projectId, baseUrl);
    }

    public async Task RevokeAsync(Guid userId, Guid projectId)
    {
        var tokens = await _apiTokenRepo.GetActiveByUserIdAsync(userId);
        foreach (var t in tokens.Where(t => !t.AppId.HasValue))
            await _apiTokenRepo.RevokeAsync(t.Id, "revoked");

        await _audit.LogAsync(userId, null, "claude_config.revoked", "api_tokens");
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

    private async Task<(User user, Project project, string[] permissions)> GetContextAsync(Guid userId, Guid projectId)
    {
        var project = await _projectRepo.GetByIdAsync(projectId)
            ?? throw new KeyNotFoundException($"Project {projectId} not found.");

        var member = await _memberRepo.GetAsync(projectId, userId)
            ?? throw new UnauthorizedAccessException("You are not a member of this project.");

        var role = await _roleRepo.GetByIdAsync(projectId, member.ProjectRoleId);
        var permissions = role?.Permissions ?? [];

        var user = await _userRepo.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        return (user, project, permissions);
    }

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
        sb.AppendLine($"GET {baseUrl}/api/projects/{project.Id}/schema/full");
        sb.AppendLine();
        sb.AppendLine("### Create Table");
        sb.AppendLine($"POST {baseUrl}/api/projects/{project.Id}/migration/create-table");
        sb.AppendLine();
        sb.AppendLine("### Read Query");
        sb.AppendLine($"POST {baseUrl}/api/projects/{project.Id}/query/read");
        sb.AppendLine();
        sb.AppendLine("### Write Query");
        sb.AppendLine($"POST {baseUrl}/api/projects/{project.Id}/query/write");
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
