using System.Text;
using FlatPlanet.Platform.Application.DTOs.Auth;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ClaudeConfigService : IClaudeConfigService
{
    private readonly IProjectRepository _projectRepo;
    private readonly IProjectMemberRepository _memberRepo;
    private readonly IProjectRoleRepository _roleRepo;
    private readonly IUserRepository _userRepo;
    private readonly IClaudeTokenRepository _claudeTokenRepo;
    private readonly IJwtService _jwtService;
    private readonly IAuditService _audit;

    public ClaudeConfigService(
        IProjectRepository projectRepo,
        IProjectMemberRepository memberRepo,
        IProjectRoleRepository roleRepo,
        IUserRepository userRepo,
        IClaudeTokenRepository claudeTokenRepo,
        IJwtService jwtService,
        IAuditService audit)
    {
        _projectRepo = projectRepo;
        _memberRepo = memberRepo;
        _roleRepo = roleRepo;
        _userRepo = userRepo;
        _claudeTokenRepo = claudeTokenRepo;
        _jwtService = jwtService;
        _audit = audit;
    }

    public async Task<ClaudeConfigResponse> GenerateAsync(Guid userId, Guid projectId, string baseUrl)
    {
        var (user, project, permissions) = await GetContextAsync(userId, projectId);

        var rawToken = _jwtService.GenerateClaudeToken(user, project, permissions, out var expiresAt);

        var stored = await _claudeTokenRepo.CreateAsync(new ClaudeToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProjectId = projectId,
            TokenHash = HashToken(rawToken),
            ExpiresAt = expiresAt,
            Revoked = false,
            CreatedAt = DateTime.UtcNow
        });

        await _audit.LogAsync(userId, projectId, "claude_config.generated", "claude_tokens",
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
        await _claudeTokenRepo.RevokeAllByUserProjectAsync(userId, projectId);

        await _audit.LogAsync(userId, projectId, "claude_config.revoked_all", "claude_tokens");

        return await GenerateAsync(userId, projectId, baseUrl);
    }

    public async Task RevokeAsync(Guid userId, Guid projectId)
    {
        await _claudeTokenRepo.RevokeAllByUserProjectAsync(userId, projectId);
        await _audit.LogAsync(userId, projectId, "claude_config.revoked", "claude_tokens");
    }

    public async Task<IEnumerable<ClaudeTokenSummaryDto>> ListActiveTokensAsync(Guid userId)
    {
        var tokens = await _claudeTokenRepo.GetActiveByUserIdAsync(userId);
        var summaries = new List<ClaudeTokenSummaryDto>();

        foreach (var token in tokens)
        {
            var project = await _projectRepo.GetByIdAsync(token.ProjectId);
            summaries.Add(new ClaudeTokenSummaryDto
            {
                TokenId = token.Id,
                ProjectId = token.ProjectId,
                ProjectName = project?.Name ?? string.Empty,
                ExpiresAt = token.ExpiresAt,
                CreatedAt = token.CreatedAt
            });
        }

        return summaries;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(User user, Project project, string[] permissions)> GetContextAsync(
        Guid userId, Guid projectId)
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

    private static string HashToken(string rawToken)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
        sb.AppendLine("Before writing any database-related code, read the current schema.");
        sb.AppendLine();
        sb.AppendLine($"GET {baseUrl}/api/projects/{project.Id}/schema/full");
        sb.AppendLine();
        sb.AppendLine("### Create Table");
        sb.AppendLine($"POST {baseUrl}/api/projects/{project.Id}/migration/create-table");
        sb.AppendLine("Content-Type: application/json");
        sb.AppendLine();
        sb.AppendLine("""
            {
              "tableName": "table_name",
              "columns": [
                { "name": "id", "type": "uuid", "isPrimaryKey": true, "default": "gen_random_uuid()" },
                { "name": "name", "type": "text", "nullable": false },
                { "name": "created_at", "type": "timestamptz", "default": "now()" }
              ],
              "enableRls": true
            }
            """);
        sb.AppendLine("### Alter Table");
        sb.AppendLine($"PUT {baseUrl}/api/projects/{project.Id}/migration/alter-table");
        sb.AppendLine("Content-Type: application/json");
        sb.AppendLine();
        sb.AppendLine("""
            {
              "tableName": "table_name",
              "actions": [
                { "action": "add", "columnName": "new_col", "type": "text" },
                { "action": "drop", "columnName": "old_col" },
                { "action": "rename", "columnName": "old_name", "newName": "new_name" },
                { "action": "alter_type", "columnName": "col", "type": "integer" }
              ]
            }
            """);
        sb.AppendLine("### Drop Table");
        sb.AppendLine($"DELETE {baseUrl}/api/projects/{project.Id}/migration/drop-table?table={{name}}");
        sb.AppendLine();
        sb.AppendLine("### Read Query");
        sb.AppendLine($"POST {baseUrl}/api/projects/{project.Id}/query/read");
        sb.AppendLine("Content-Type: application/json");
        sb.AppendLine();
        sb.AppendLine("""
            {
              "sql": "SELECT * FROM table_name WHERE column = @param LIMIT @limit",
              "parameters": { "param": "value", "limit": 50 }
            }
            """);
        sb.AppendLine("### Write Query");
        sb.AppendLine($"POST {baseUrl}/api/projects/{project.Id}/query/write");
        sb.AppendLine("Content-Type: application/json");
        sb.AppendLine();
        sb.AppendLine("""
            {
              "sql": "INSERT INTO table_name (col1, col2) VALUES (@val1, @val2)",
              "parameters": { "val1": "hello", "val2": "world" }
            }
            """);
        sb.AppendLine("## Rules");
        sb.AppendLine("1. ALWAYS read the schema first before writing any database code");
        sb.AppendLine("2. ALWAYS use parameterized queries — use @paramName syntax, NEVER concatenate values into SQL");
        sb.AppendLine("3. Use migration endpoints for CREATE/ALTER/DROP — never raw DDL in query endpoints");
        sb.AppendLine("4. Check the \"success\" field in every API response — handle errors gracefully");
        sb.AppendLine("5. All database access must go through the API — NEVER connect to the database directly");
        sb.AppendLine();
        sb.AppendLine("## Git Workflow");
        sb.AppendLine("1. Work on a feature branch: git checkout -b feature/{feature-name}");
        sb.AppendLine("2. Build and test locally before committing");
        sb.AppendLine("3. Commit with descriptive messages: feat:, fix:, refactor:, docs:");
        sb.AppendLine("4. Push: git push origin feature/{feature-name}");
        sb.AppendLine("5. For major features, create a PR to main");
        sb.AppendLine();
        sb.AppendLine("## IMPORTANT");
        sb.AppendLine("- This file is LOCAL ONLY — do not commit or push this file");
        sb.AppendLine("- If the token has expired, ask the user to regenerate it from the app");

        return sb.ToString();
    }
}
