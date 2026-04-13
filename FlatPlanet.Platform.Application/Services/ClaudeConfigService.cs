using System.Text;
using FlatPlanet.Platform.Application.Common.Helpers;
using FlatPlanet.Platform.Application.Common.Options;
using FlatPlanet.Platform.Application.DTOs.Auth;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ClaudeConfigService : IClaudeConfigService
{
    public const string TemplateVersion = "1.2.0";

    private readonly IProjectRepository _projectRepo;
    private readonly IApiTokenRepository _apiTokenRepo;
    private readonly IJwtService _jwtService;
    private readonly IAuditService _audit;
    private readonly ISecurityPlatformService _securityPlatform;
    private readonly GitHubOptions _github;

    public ClaudeConfigService(
        IProjectRepository projectRepo,
        IApiTokenRepository apiTokenRepo,
        IJwtService jwtService,
        IAuditService audit,
        ISecurityPlatformService securityPlatform,
        IOptions<GitHubOptions> github)
    {
        _projectRepo = projectRepo;
        _apiTokenRepo = apiTokenRepo;
        _jwtService = jwtService;
        _audit = audit;
        _securityPlatform = securityPlatform;
        _github = github.Value;
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
            Content = RenderTemplate(project, rawToken, expiresAt, baseUrl, _github),
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

    public async Task<WorkspaceResponse> GetWorkspaceAsync(Guid userId, Guid projectId, string baseUrl, string userName, string userEmail)
    {
        var project = await _projectRepo.GetByIdAsync(projectId)
            ?? throw new KeyNotFoundException($"Project {projectId} not found.");

        if (project.AppId is null)
            throw new InvalidOperationException($"Project {projectId} is not linked to an IAM app.");

        var existing = await _apiTokenRepo.GetActiveByUserIdAsync(userId);
        var hasActiveToken = existing.Any(t => t.AppId == project.AppId);

        var config = hasActiveToken
            ? await RegenerateAsync(userId, projectId, baseUrl, userName, userEmail)
            : await GenerateAsync(userId, projectId, baseUrl, userName, userEmail);

        return new WorkspaceResponse
        {
            Content   = config.Content,
            Filename  = "CLAUDE-local.md",
            GitignoreEntry = "CLAUDE-local.md",
            TokenId   = config.TokenId,
            ExpiresAt = config.ExpiresAt
        };
    }

    public async Task<(string RawToken, string RenderedMarkdown)> RenderAndStoreTokenAsync(Project project, Guid userId, string actorEmail, string baseUrl)
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

        var renderedMarkdown = RenderTemplate(project, rawToken, expiresAt, baseUrl, _github);
        return (rawToken, renderedMarkdown);
    }

    public ClaudeConfigVersionResponse GetTemplateVersion() =>
        new(TemplateVersion);

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

    private static string BuildCodingStandards(Project project)
    {
        var sb = new StringBuilder();

        switch (project.ProjectType.ToLowerInvariant())
        {
            case "frontend":
                sb.AppendLine("### Frontend Standards (React / TypeScript)");
                sb.AppendLine("- React.js with TypeScript (latest version)");
                sb.AppendLine("- Strict TypeScript — no `any`, explicit return types on all functions");
                sb.AppendLine("- Component naming: PascalCase, one component per file");
                sb.AppendLine("- Hooks: prefix with `use`, keep side effects in useEffect only");
                sb.AppendLine("- State management: follow existing pattern in the codebase");
                sb.AppendLine("- Folder structure: feature-based (components, hooks, services, types per feature)");
                sb.AppendLine("- API calls: always through a service layer — never fetch directly in components");
                sb.AppendLine("- Error boundaries: wrap major sections");
                sb.AppendLine("- No unused imports, no console.log in production code");
                sb.AppendLine("- Deploy target: Netlify");
                break;

            case "backend":
                sb.AppendLine("### Backend Standards (.NET 10 / C#)");
                sb.AppendLine("- .NET 10 / C# — use latest language features (primary constructors, pattern matching, etc.)");
                sb.AppendLine("- Clean Architecture: Controller → Application Service → Domain → Infrastructure");
                sb.AppendLine("- SOLID principles enforced");
                sb.AppendLine("- Dependency Injection for all services — never instantiate dependencies manually");
                sb.AppendLine("- Apply design patterns where appropriate: Strategy, Chain of Responsibility, Factory, Decorator");
                sb.AppendLine("- No EF Core — Dapper only, raw SQL via IDbConnectionFactory");
                sb.AppendLine("- All async/await — no blocking calls (.Result, .Wait())");
                sb.AppendLine("- GlobalExceptionMiddleware handles all errors — never swallow exceptions silently");
                sb.AppendLine("- Always run `dotnet build` before committing");
                sb.AppendLine("- Deploy target: Azure App Service");
                break;

            case "database":
                sb.AppendLine("### Database Standards (Supabase / PostgreSQL)");
                sb.AppendLine("- Supabase / PostgreSQL");
                sb.AppendLine("- ALWAYS check the data dictionary before naming anything (Step 0)");
                sb.AppendLine("- ALWAYS read the schema before writing DB code (Step 1)");
                sb.AppendLine("- All DDL goes through migration endpoints — never raw DDL in query endpoints");
                sb.AppendLine("- All queries use @paramName — never concatenate values into SQL");
                sb.AppendLine("- snake_case for all table and column names");
                sb.AppendLine("- UUID primary keys with gen_random_uuid()");
                sb.AppendLine("- Always include created_at TIMESTAMPTZ DEFAULT now()");
                sb.AppendLine("- Soft deletes preferred — use is_active boolean over hard deletes");
                sb.AppendLine("- Always add indexes on foreign keys and frequently queried columns");
                break;

            case "fullstack":
            default:
                sb.AppendLine("### Frontend Standards (React / TypeScript)");
                sb.AppendLine("- React.js with TypeScript (latest version)");
                sb.AppendLine("- Strict TypeScript — no `any`, explicit return types on all functions");
                sb.AppendLine("- Component naming: PascalCase, one component per file");
                sb.AppendLine("- Hooks: prefix with `use`, keep side effects in useEffect only");
                sb.AppendLine("- State management: follow existing pattern in the codebase");
                sb.AppendLine("- Folder structure: feature-based (components, hooks, services, types per feature)");
                sb.AppendLine("- API calls: always through a service layer — never fetch directly in components");
                sb.AppendLine("- Error boundaries: wrap major sections");
                sb.AppendLine("- No unused imports, no console.log in production code");
                sb.AppendLine("- Deploy target: Netlify");
                sb.AppendLine();
                sb.AppendLine("### Backend Standards (.NET 10 / C#)");
                sb.AppendLine("- .NET 10 / C# — use latest language features (primary constructors, pattern matching, etc.)");
                sb.AppendLine("- Clean Architecture: Controller → Application Service → Domain → Infrastructure");
                sb.AppendLine("- SOLID principles enforced");
                sb.AppendLine("- Dependency Injection for all services — never instantiate dependencies manually");
                sb.AppendLine("- Apply design patterns where appropriate: Strategy, Chain of Responsibility, Factory, Decorator");
                sb.AppendLine("- No EF Core — Dapper only, raw SQL via IDbConnectionFactory");
                sb.AppendLine("- All async/await — no blocking calls (.Result, .Wait())");
                sb.AppendLine("- GlobalExceptionMiddleware handles all errors — never swallow exceptions silently");
                sb.AppendLine("- Always run `dotnet build` before committing");
                sb.AppendLine("- Deploy target: Azure App Service");
                sb.AppendLine();
                sb.AppendLine("### Database Standards (Supabase / PostgreSQL)");
                sb.AppendLine("- Supabase / PostgreSQL");
                sb.AppendLine("- ALWAYS check the data dictionary before naming anything (Step 0)");
                sb.AppendLine("- ALWAYS read the schema before writing DB code (Step 1)");
                sb.AppendLine("- All DDL goes through migration endpoints — never raw DDL in query endpoints");
                sb.AppendLine("- All queries use @paramName — never concatenate values into SQL");
                sb.AppendLine("- snake_case for all table and column names");
                sb.AppendLine("- UUID primary keys with gen_random_uuid()");
                sb.AppendLine("- Always include created_at TIMESTAMPTZ DEFAULT now()");
                sb.AppendLine("- Soft deletes preferred — use is_active boolean over hard deletes");
                sb.AppendLine("- Always add indexes on foreign keys and frequently queried columns");
                break;
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private static string RenderTemplate(Project project, string token, DateTime expiresAt, string baseUrl, GitHubOptions github)
    {
        var pid = project.Id;
        var api = $"{baseUrl}/api/projects/{pid}";
        var sb = new StringBuilder();

        sb.AppendLine("<!-- ⚠️  DO NOT COMMIT THIS FILE ⚠️  -->");
        sb.AppendLine("<!-- This file contains a live API token scoped to your project database. -->");
        sb.AppendLine("<!-- Add CLAUDE-local.md to your .gitignore immediately if not already done. -->");
        sb.AppendLine("<!-- Regenerate from the FlatPlanet Hub if this token is ever exposed.      -->");
        sb.AppendLine($"<!-- template-version: {TemplateVersion} -->");
        sb.AppendLine();
        sb.AppendLine("> **⚠️ LOCAL FILE — DO NOT COMMIT**");
        sb.AppendLine("> This file is git-ignored for a reason. It contains a **live API token** tied to your project's database.");
        sb.AppendLine("> If you accidentally commit this file, go to the FlatPlanet Hub immediately and click **Regenerate** to revoke the token.");
        sb.AppendLine("> Add this entry to your `.gitignore`: `CLAUDE-local.md`");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Session Startup — Read This First");
        sb.AppendLine();
        sb.AppendLine("### Step 1 — Check for template updates");
        sb.AppendLine("Call this endpoint to check if your CLAUDE-local.md is up to date:");
        sb.AppendLine($"GET {baseUrl}/api/projects/{pid}/claude-config/version");
        sb.AppendLine($"Header: Authorization: Bearer {token}");
        sb.AppendLine();
        sb.AppendLine($"Compare the returned `templateVersion` against this file's version: {TemplateVersion}");
        sb.AppendLine();
        sb.AppendLine("If the returned version is NEWER than this file's version:");
        sb.AppendLine("  Stop and tell the user:");
        sb.AppendLine($"  \"Your CLAUDE-local.md is outdated (this file: {TemplateVersion}, latest: <returned version>).");
        sb.AppendLine("   Please go to the FlatPlanet Hub dashboard, click Regenerate on this project,");
        sb.AppendLine("   download the new file, and replace CLAUDE-local.md before continuing.\"");
        sb.AppendLine("  Do NOT proceed with any work until the user confirms they have updated the file.");
        sb.AppendLine();
        sb.AppendLine("If versions match — proceed to Step 2.");
        sb.AppendLine();
        sb.AppendLine("### Step 2 — Read session log");
        sb.AppendLine("Read `CONVERSATION-LOG.md` in the project root.");
        sb.AppendLine("This is Claude's memory across sessions — current state, decisions made, open issues, what to do next.");
        sb.AppendLine("If the file does not exist yet, create it before closing the session.");
        sb.AppendLine("At the end of every session, append a new entry to `CONVERSATION-LOG.md` before committing.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("# Project Context");
        sb.AppendLine();
        sb.AppendLine("## Project");
        sb.AppendLine($"- **Name**: {project.Name}");
        sb.AppendLine($"- **Description**: {project.Description ?? string.Empty}");
        sb.AppendLine($"- **Project ID**: {pid}");
        sb.AppendLine($"- **Schema**: {project.SchemaName}");
        sb.AppendLine($"- **Tech Stack**: {project.TechStack ?? string.Empty}");
        sb.AppendLine($"- **Project Type**: {project.ProjectType}");
        sb.AppendLine($"- **Auth Enabled**: {project.AuthEnabled}");
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
        sb.AppendLine("## Platform API Capabilities");
        sb.AppendLine();
        sb.AppendLine("The Platform API provides shared services for all FlatPlanet projects.");
        sb.AppendLine("Use the token above as Bearer on all calls to these endpoints.");
        sb.AppendLine();
        sb.AppendLine("### File Storage");
        sb.AppendLine("Files are stored centrally in Azure Blob Storage, scoped by business code.");
        sb.AppendLine("SAS URLs are time-limited (60 min) — always fetch a fresh URL before displaying.");
        sb.AppendLine();
        sb.AppendLine("Upload a file:");
        sb.AppendLine($"POST {baseUrl}/api/v1/storage/upload");
        sb.AppendLine("Content-Type: multipart/form-data");
        sb.AppendLine("Fields: file (binary), businessCode (e.g. \"fp\"), category (e.g. \"logos\"), tags (comma-separated, optional)");
        sb.AppendLine("Returns: { fileId, sasUrl, sasExpiresAt, businessCode, category, originalName, fileSizeBytes, tags }");
        sb.AppendLine();
        sb.AppendLine("List files:");
        sb.AppendLine($"GET {baseUrl}/api/v1/storage/files?businessCode=fp&category=logos&tags=primary");
        sb.AppendLine("Returns: array of file objects each with a fresh sasUrl");
        sb.AppendLine();
        sb.AppendLine("Get a fresh SAS URL for an existing file:");
        sb.AppendLine($"GET {baseUrl}/api/v1/storage/files/{{fileId}}/url");
        sb.AppendLine("Returns: { sasUrl, expiresAt }");
        sb.AppendLine();
        sb.AppendLine("Delete a file:");
        sb.AppendLine($"DELETE {baseUrl}/api/v1/storage/files/{{fileId}}");
        sb.AppendLine("Returns: 204 No Content");
        sb.AppendLine();
        sb.AppendLine("### Business Membership");
        sb.AppendLine("The JWT contains a business_codes[] claim (e.g. [\"fp\"]) — use this to filter content per business.");
        sb.AppendLine("Do NOT hardcode business IDs — always use the code from the JWT claim.");
        sb.AppendLine();
        sb.AppendLine("Read business_codes from the decoded JWT:");
        sb.AppendLine("  const codes = jwt.business_codes; // [\"fp\"]");
        sb.AppendLine("  const isFlatPlanet = codes.includes(\"fp\");");
        sb.AppendLine();
        sb.AppendLine("### Full API Reference");
        sb.AppendLine("For complete endpoint docs, request/response schemas, and error codes:");
        sb.AppendLine("  Platform API:      https://github.com/FlatPlanet-Hub/platform-api/blob/main/docs/platform-api-reference.md");
        sb.AppendLine("  Security Platform: https://github.com/FlatPlanet-Hub/flatplanet-security-platform/blob/main/docs/security-api-reference.md");
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
        sb.AppendLine("    { \"type\": \"AddColumn\", \"columnName\": \"new_col\", \"dataType\": \"text\" },");
        sb.AppendLine("    { \"type\": \"DropColumn\", \"columnName\": \"old_col\" },");
        sb.AppendLine("    { \"type\": \"RenameColumn\", \"columnName\": \"old_name\", \"newColumnName\": \"new_name\" }");
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
        sb.AppendLine("7. If the token has expired, ask the user to regenerate CLAUDE-local.md from the FlatPlanet Hub");
        sb.AppendLine();
        sb.AppendLine("## Git Workflow");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(project.GitHubRepoName) && !string.IsNullOrWhiteSpace(github.ServiceToken) && !string.IsNullOrWhiteSpace(github.OrgName))
        {
            var cloneUrl = $"https://{github.ServiceToken}@github.com/{github.OrgName}/{project.GitHubRepoName}.git";
            sb.AppendLine("### IMPORTANT — Git Authentication");
            sb.AppendLine("The clone URL below is pre-authenticated using a platform service token.");
            sb.AppendLine("NEVER ask the user for GitHub credentials, username, password, or personal access token.");
            sb.AppendLine("ALWAYS use the exact URL below for all git operations that require authentication.");
            sb.AppendLine("The user does NOT have a GitHub account — the service token handles all access.");
            sb.AppendLine();
            sb.AppendLine("### Clone the repo");
            sb.AppendLine($"git clone {cloneUrl}");
            sb.AppendLine();
            sb.AppendLine("### Push / pull (use the same token-embedded remote)");
            sb.AppendLine($"git remote set-url origin {cloneUrl}");
            sb.AppendLine();
            sb.AppendLine("Never share or commit this URL — it contains a live token.");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("No GitHub repo linked to this project yet.");
            sb.AppendLine();
        }
        sb.AppendLine("1. Work on a feature branch: git checkout -b feature/{feature-name}");
        sb.AppendLine("2. Build and test locally before committing");
        sb.AppendLine("3. Commit with descriptive messages: feat:, fix:, refactor:, docs:");
        sb.AppendLine("4. Push: git push origin feature/{feature-name}");
        sb.AppendLine("5. For major features, create a PR to main");
        sb.AppendLine();
        sb.AppendLine("## Coding Standards");
        sb.AppendLine();
        sb.Append(BuildCodingStandards(project));
        sb.AppendLine("- Clean, readable code — add comments only where logic is non-obvious");
        sb.AppendLine("- Handle errors gracefully — never swallow exceptions silently");
        sb.AppendLine("- Follow naming conventions of the existing codebase");
        sb.AppendLine();

        sb.AppendLine("## Project Management");
        sb.AppendLine();
        sb.AppendLine("Use these endpoints (with your SP JWT, not the API token) to manage this project:");
        sb.AppendLine();
        sb.AppendLine("### Enable Authentication on this project");
        sb.AppendLine($"PUT {baseUrl}/api/projects/{pid}");
        sb.AppendLine("Header: Authorization: Bearer <SP JWT>");
        sb.AppendLine("Body: { \"authEnabled\": true }");
        sb.AppendLine("After enabling: regenerate this file to get the SP auth integration guide injected.");
        sb.AppendLine();
        sb.AppendLine("### Regenerate this workspace file");
        sb.AppendLine($"POST {baseUrl}/api/projects/{pid}/claude-config/regenerate");
        sb.AppendLine("Header: Authorization: Bearer <SP JWT>");
        sb.AppendLine("Returns a fresh CLAUDE-local.md with a new token. Ask the user to save the new file.");
        sb.AppendLine();

        const string spBaseUrl = "https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net";
        var appSlug = project.AppSlug ?? project.SchemaName;

        sb.AppendLine("## FlatPlanet Security Platform (SP)");
        sb.AppendLine();
        sb.AppendLine("All FlatPlanet projects use the Security Platform for authentication and authorization.");
        sb.AppendLine("NEVER build your own auth system — always use the SP endpoints below.");
        sb.AppendLine();
        sb.AppendLine($"SP Base URL:  {spBaseUrl}");
        sb.AppendLine($"App Slug:     {appSlug}");
        sb.AppendLine($"App ID:       {project.AppId}");
        sb.AppendLine($"JWT Issuer:   flatplanet-security");
        sb.AppendLine($"JWT Audience: flatplanet-apps");
        sb.AppendLine($"Auth Status:  {(project.AuthEnabled ? "ENABLED — auth is active on this project" : "DISABLED — enable when ready (see Project Management above)")}");
        sb.AppendLine();
        sb.AppendLine("### Login");
        sb.AppendLine($"POST {spBaseUrl}/api/v1/auth/login");
        sb.AppendLine($"Body: {{ \"email\": \"...\", \"password\": \"...\", \"appSlug\": \"{appSlug}\" }}");
        sb.AppendLine("Returns: { accessToken (60 min), refreshToken, expiresIn, user }");
        sb.AppendLine();
        sb.AppendLine("### Protect Routes");
        sb.AppendLine("All protected routes require:");
        sb.AppendLine("  Authorization: Bearer <accessToken>");
        sb.AppendLine("On 401 → try refresh. If refresh fails → redirect to login.");
        sb.AppendLine();
        sb.AppendLine("### Check Permission");
        sb.AppendLine($"POST {spBaseUrl}/api/v1/authorize");
        sb.AppendLine($"Body: {{ \"appSlug\": \"{appSlug}\", \"resourceIdentifier\": \"/your-route\", \"requiredPermission\": \"read\" }}");
        sb.AppendLine("Returns: { allowed: true/false } — HTTP 200 either way. allowed: false → show no-access page.");
        sb.AppendLine();
        sb.AppendLine("### Refresh Token");
        sb.AppendLine($"POST {spBaseUrl}/api/v1/auth/refresh");
        sb.AppendLine("Body: { \"refreshToken\": \"...\" }");
        sb.AppendLine("Single-use — store the new token immediately. On 401 → redirect to login.");
        sb.AppendLine();
        sb.AppendLine("### Logout");
        sb.AppendLine($"POST {spBaseUrl}/api/v1/auth/logout");
        sb.AppendLine("Revokes all refresh tokens. Clear both tokens client-side and redirect to login.");
        sb.AppendLine();

        // Azure Deployment Section
        sb.AppendLine("## Azure Deployment");
        sb.AppendLine();
        if (string.IsNullOrWhiteSpace(project.AzureAppServiceName))
        {
            sb.AppendLine("Status: NOT PROVISIONED");
            sb.AppendLine();
            sb.AppendLine("This project has no Azure App Service yet.");
            sb.AppendLine();
            sb.AppendLine("To provision, tell Claude Code:");
            sb.AppendLine("  \"provision Azure for this project\"");
            sb.AppendLine();
            sb.AppendLine("Claude Code will call:");
            sb.AppendLine($"  POST {baseUrl}/api/projects/{pid}/provision-azure");
            sb.AppendLine("And update this file automatically once complete.");
        }
        else
        {
            sb.AppendLine("Status: PROVISIONED");
            sb.AppendLine($"App Service:      {project.AzureAppServiceName}");
            sb.AppendLine($"App Service URL:  {project.AzureAppServiceUrl ?? "pending"}");
            sb.AppendLine("Resource Group:   FPPlatform");
            sb.AppendLine("Region:           southeastasia");
            sb.AppendLine();
            sb.AppendLine("### Standard env vars (set automatically at provision time)");
            sb.AppendLine("These were configured automatically — you do not need to set these:");
            sb.AppendLine();
            sb.AppendLine("  Jwt__SecretKey              (SP signing secret)");
            sb.AppendLine("  Jwt__Issuer                 flatplanet-security");
            sb.AppendLine("  Jwt__Audience               flatplanet-apps");
            sb.AppendLine($"  PlatformApi__BaseUrl        {baseUrl}");
            sb.AppendLine("  PlatformApi__Token          (project scoped token — check portal if missing)");
            sb.AppendLine($"  ConnectionStrings__Default  (Supabase, scoped to schema {project.SchemaName})");
            sb.AppendLine();
            sb.AppendLine("### Project-specific env vars (set these manually in Azure Portal)");
            sb.AppendLine($"Navigate to: Azure Portal → {project.AzureAppServiceName} → Environment variables");
            sb.AppendLine();
            sb.AppendLine("Add any secrets your project needs, for example:");
            sb.AppendLine("  SendGrid__ApiKey");
            sb.AppendLine("  Stripe__SecretKey");
            sb.AppendLine("  Twilio__AuthToken");
            sb.AppendLine("  ExternalApi__Key");
            sb.AppendLine();
            sb.AppendLine("### Deploy");
            sb.AppendLine("Deployment is handled automatically by GitHub Actions on every push to `main`.");
            sb.AppendLine("Do NOT install `az` CLI or run `dotnet publish` manually — just push your code:");
            sb.AppendLine();
            sb.AppendLine("  git push origin main");
            sb.AppendLine();
            sb.AppendLine("GitHub Actions will build, test, and deploy to Azure App Service automatically.");
            sb.AppendLine("To deploy a feature branch, merge it into `main` first (or open a PR to main).");
        }

        sb.AppendLine();
        return sb.ToString();
    }
}
