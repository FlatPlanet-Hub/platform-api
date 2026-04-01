using System.Text;
using FlatPlanet.Platform.Application.DTOs.Auth;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using FlatPlanet.Platform.Application.Common.Helpers;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ClaudeConfigService : IClaudeConfigService
{
    private readonly IProjectRepository _projectRepo;
    private readonly IApiTokenRepository _apiTokenRepo;
    private readonly IJwtService _jwtService;
    private readonly IAuditService _audit;
    private readonly ISecurityPlatformService _securityPlatform;

    public ClaudeConfigService(
        IProjectRepository projectRepo,
        IApiTokenRepository apiTokenRepo,
        IJwtService jwtService,
        IAuditService audit,
        ISecurityPlatformService securityPlatform)
    {
        _projectRepo = projectRepo;
        _apiTokenRepo = apiTokenRepo;
        _jwtService = jwtService;
        _audit = audit;
        _securityPlatform = securityPlatform;
    }

    public async Task<ClaudeConfigResponse> GenerateAsync(Guid userId, Guid projectId, string baseUrl, string userName, string userEmail)
    {
        var (project, permissions) = await GetContextAsync(userId, projectId);

        var rawToken = _jwtService.GenerateApiToken(
            userId, userName, userEmail,
            project.AppId, project.AppSlug ?? project.SchemaName, project.SchemaName,
            permissions, 30, out var expiresAt);

        var tokenHash = TokenHasher.Hash(rawToken);

        var stored = await _apiTokenRepo.CreateAsync(new ApiToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AppId = project.AppId,
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

    public async Task<ClaudeConfigResponse> RegenerateAsync(Guid userId, Guid projectId, string baseUrl, string userName, string userEmail)
    {
        var project = await _projectRepo.GetByIdAsync(projectId)
            ?? throw new KeyNotFoundException($"Project {projectId} not found.");

        if (project.AppId is null)
            throw new InvalidOperationException($"Project {projectId} is not linked to an IAM app.");

        var tokens = await _apiTokenRepo.GetActiveByUserIdAsync(userId);
        foreach (var t in tokens.Where(t => t.AppId == project.AppId))
            await _apiTokenRepo.RevokeAsync(t.Id, "regenerated");

        await _audit.LogAsync(userId, project.AppId, "claude_config.revoked_all", "api_tokens");
        return await GenerateAsync(userId, projectId, baseUrl, userName, userEmail);
    }

    public async Task RevokeAsync(Guid userId, Guid projectId)
    {
        var project = await _projectRepo.GetByIdAsync(projectId)
            ?? throw new KeyNotFoundException($"Project {projectId} not found.");

        if (project.AppId is null)
            throw new InvalidOperationException($"Project {projectId} is not linked to an IAM app.");

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

    public async Task<string> RenderAndStoreTokenAsync(Project project, Guid userId, string actorEmail, string baseUrl)
    {
        var appAccess = await _securityPlatform.GetUserAppAccessAsync(userId);
        var roleEntry = appAccess.FirstOrDefault(r => r.AppId == project.AppId);
        var permissions = roleEntry?.Permissions ?? ["read", "write", "ddl"];

        var rawToken = _jwtService.GenerateApiToken(
            userId, actorEmail, actorEmail,
            project.AppId, project.AppSlug ?? project.SchemaName, project.SchemaName,
            permissions, 30, out var expiresAt);

        var tokenHash = TokenHasher.Hash(rawToken);

        await _apiTokenRepo.CreateAsync(new ApiToken
        {
            Id          = Guid.NewGuid(),
            UserId      = userId,
            AppId       = project.AppId,
            Name        = $"Claude token for {project.Name}",
            TokenHash   = tokenHash,
            Permissions = permissions,
            ExpiresAt   = expiresAt,
            Revoked     = false,
            CreatedAt   = DateTime.UtcNow
        });

        return RenderTemplate(project, rawToken, expiresAt, baseUrl);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(Project project, string[] permissions)> GetContextAsync(Guid userId, Guid projectId)
    {
        var project = await _projectRepo.GetByIdAsync(projectId)
            ?? throw new KeyNotFoundException($"Project {projectId} not found.");

        if (project.AppId is null)
            throw new InvalidOperationException($"Project {projectId} is not linked to an IAM app.");

        var appAccess = await _securityPlatform.GetUserAppAccessAsync(userId);
        var roleEntry = appAccess.FirstOrDefault(r => r.AppId == project.AppId.Value);

        if (roleEntry is null)
            throw new UnauthorizedAccessException("You do not have access to this project.");

        return (project, roleEntry.Permissions);
    }

    private static string RenderTemplate(Project project, string token, DateTime expiresAt, string baseUrl)
    {
        var pid = project.Id;
        var api = $"{baseUrl}/api/projects/{pid}";
        var sb = new StringBuilder();

        sb.AppendLine("# Project Context");
        sb.AppendLine();
        sb.AppendLine("## Project");
        sb.AppendLine($"- **Name**: {project.Name}");
        sb.AppendLine($"- **Description**: {project.Description ?? string.Empty}");
        sb.AppendLine($"- **Project ID**: {pid}");
        sb.AppendLine($"- **Schema**: {project.SchemaName}");
        sb.AppendLine($"- **Tech Stack**: {project.TechStack ?? string.Empty}");
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
        sb.AppendLine("## Working With the Database");
        sb.AppendLine();
        sb.AppendLine("### Step 0 — Check the naming dictionary BEFORE naming anything");
        sb.AppendLine("Before creating any table, column, variable, or function, query the data dictionary");
        sb.AppendLine("to find the approved standard name for the concept you are working with.");
        sb.AppendLine();
        sb.AppendLine("Search by concept:");
        sb.AppendLine($"POST {api}/query/read");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"sql\": \"SELECT field_name, data_type, format, description, example, entity FROM data_dictionary WHERE field_name ILIKE @search OR description ILIKE @search OR entity ILIKE @search ORDER BY field_name LIMIT 20\",");
        sb.AppendLine("  \"parameters\": { \"search\": \"%<concept>%\" }");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("If the standard name exists — use it exactly as recorded in `field_name`.");
        sb.AppendLine("If no matching entry exists, create one before proceeding:");
        sb.AppendLine($"POST {api}/query/write");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"sql\": \"INSERT INTO data_dictionary (field_name, data_type, format, description, example, entity, category, is_required, source) VALUES (@field_name, @data_type, @format, @description, @example, @entity, @category, @is_required, 'project') ON CONFLICT DO NOTHING\",");
        sb.AppendLine("  \"parameters\": {");
        sb.AppendLine("    \"field_name\": \"snake_case_name\",");
        sb.AppendLine("    \"data_type\": \"text|uuid|timestamptz|boolean|numeric|...\",");
        sb.AppendLine("    \"format\": null,");
        sb.AppendLine("    \"description\": \"What this field represents\",");
        sb.AppendLine("    \"example\": \"example_value\",");
        sb.AppendLine("    \"entity\": \"the table or entity this belongs to\",");
        sb.AppendLine("    \"category\": \"field\",");
        sb.AppendLine("    \"is_required\": false");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("### Step 1 — Read the project schema");
        sb.AppendLine($"GET {api}/schema/full");
        sb.AppendLine();
        sb.AppendLine("Returns all tables, columns, types, and foreign key relationships in this project.");
        sb.AppendLine();
        sb.AppendLine("### Create a Table");
        sb.AppendLine($"POST {api}/migration/create-table");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"tableName\": \"table_name\",");
        sb.AppendLine("  \"columns\": [");
        sb.AppendLine("    { \"name\": \"id\", \"type\": \"uuid\", \"isPrimaryKey\": true, \"default\": \"gen_random_uuid()\" },");
        sb.AppendLine("    { \"name\": \"name\", \"type\": \"text\", \"nullable\": false },");
        sb.AppendLine("    { \"name\": \"created_at\", \"type\": \"timestamptz\", \"default\": \"now()\" }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"enableRls\": true");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("### Alter a Table");
        sb.AppendLine($"PUT {api}/migration/alter-table");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"tableName\": \"table_name\",");
        sb.AppendLine("  \"operations\": [");
        sb.AppendLine("    { \"action\": \"add\", \"columnName\": \"new_col\", \"type\": \"text\" },");
        sb.AppendLine("    { \"action\": \"drop\", \"columnName\": \"old_col\" },");
        sb.AppendLine("    { \"action\": \"rename\", \"columnName\": \"old_name\", \"newName\": \"new_name\" }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("### Drop a Table");
        sb.AppendLine($"DELETE {api}/migration/drop-table?table={{name}}");
        sb.AppendLine();
        sb.AppendLine("### Read Query");
        sb.AppendLine($"POST {api}/query/read");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"sql\": \"SELECT * FROM table_name WHERE column = @param LIMIT @limit\",");
        sb.AppendLine("  \"parameters\": { \"param\": \"value\", \"limit\": 50 }");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("### Write Query");
        sb.AppendLine($"POST {api}/query/write");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"sql\": \"INSERT INTO table_name (col1, col2) VALUES (@val1, @val2)\",");
        sb.AppendLine("  \"parameters\": { \"val1\": \"hello\", \"val2\": \"world\" }");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Rules");
        sb.AppendLine("1. ALWAYS check the data dictionary (Step 0) before naming any table, column, variable, or function");
        sb.AppendLine("2. ALWAYS read the schema (Step 1) before writing any database-related code");
        sb.AppendLine("3. ALWAYS use @paramName syntax in queries — NEVER concatenate values into SQL strings");
        sb.AppendLine("4. Use migration endpoints for CREATE TABLE / ALTER TABLE / DROP TABLE — never raw DDL in query endpoints");
        sb.AppendLine("5. All database access goes through the API — NEVER connect to the database directly");
        sb.AppendLine("6. If an API call fails, check the \"success\" field and \"error\" message in the response");
        sb.AppendLine("7. If the token has expired, ask the user to regenerate CLAUDE.md from the Hub");
        sb.AppendLine();
        sb.AppendLine("## Git Workflow");
        sb.AppendLine("1. Work on a feature branch: git checkout -b feature/{feature-name}");
        sb.AppendLine("2. Build and test locally before committing");
        sb.AppendLine("3. Commit with descriptive messages: feat:, fix:, refactor:, docs:");
        sb.AppendLine("4. Push: git push origin feature/{feature-name}");
        sb.AppendLine("5. For major features, create a PR to main");
        sb.AppendLine();
        sb.AppendLine("## Coding Standards");
        sb.AppendLine($"- {project.TechStack ?? "Follow the existing codebase conventions"}");
        sb.AppendLine("- Clean, readable code — add comments only where logic is non-obvious");
        sb.AppendLine("- Handle errors gracefully — never swallow exceptions silently");
        sb.AppendLine("- Follow naming conventions of the existing codebase");
        sb.AppendLine();
        sb.AppendLine("## IMPORTANT");
        sb.AppendLine("- This file is LOCAL ONLY — do not commit or push it");
        sb.AppendLine("- CLAUDE.md is in .gitignore — it will not appear in git status");
        sb.AppendLine("- If the token expires, ask the user to click \"Regenerate CLAUDE.md\" in the Hub");
        return sb.ToString();
    }
}
