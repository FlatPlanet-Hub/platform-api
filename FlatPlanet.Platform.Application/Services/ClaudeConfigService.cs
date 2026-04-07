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

    private static string RenderTemplate(Project project, string token, DateTime expiresAt, string baseUrl)
    {
        var pid = project.Id;
        var api = $"{baseUrl}/api/projects/{pid}";
        var sb = new StringBuilder();

        sb.AppendLine("<!-- ⚠️  DO NOT COMMIT THIS FILE ⚠️  -->");
        sb.AppendLine("<!-- This file contains a live API token scoped to your project database. -->");
        sb.AppendLine("<!-- Add CLAUDE-local.md to your .gitignore immediately if not already done. -->");
        sb.AppendLine("<!-- Regenerate from the FlatPlanet Hub if this token is ever exposed.      -->");
        sb.AppendLine();
        sb.AppendLine("> **⚠️ LOCAL FILE — DO NOT COMMIT**");
        sb.AppendLine("> This file is git-ignored for a reason. It contains a **live API token** tied to your project's database.");
        sb.AppendLine("> If you accidentally commit this file, go to the FlatPlanet Hub immediately and click **Regenerate** to revoke the token.");
        sb.AppendLine("> Add this entry to your `.gitignore`: `CLAUDE-local.md`");
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

        if (project.AuthEnabled)
        {
            const string spBaseUrl = "https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net";
            sb.AppendLine("## Authentication (SP Integration)");
            sb.AppendLine();
            sb.AppendLine("This project uses FlatPlanet Security Platform for authentication.");
            sb.AppendLine("Do NOT build your own auth — use the endpoints below.");
            sb.AppendLine();
            sb.AppendLine($"App Slug:    {project.AppSlug ?? project.SchemaName}");
            sb.AppendLine($"App ID:      {project.AppId}");
            sb.AppendLine($"SP Base URL: {spBaseUrl}");
            sb.AppendLine();
            sb.AppendLine("### Login");
            sb.AppendLine($"POST {spBaseUrl}/api/v1/auth/login");
            sb.AppendLine("Body: { \"email\": \"...\", \"password\": \"...\" }");
            sb.AppendLine("Returns: { accessToken, refreshToken, expiresIn, user }");
            sb.AppendLine();
            sb.AppendLine("### Protect Routes");
            sb.AppendLine("- All protected routes require: Authorization: Bearer <accessToken>");
            sb.AppendLine("- JWT issuer: flatplanet-security");
            sb.AppendLine("- JWT audience: flatplanet-apps");
            sb.AppendLine("- On 401 → redirect to login");
            sb.AppendLine();
            sb.AppendLine("### Check Permission");
            sb.AppendLine($"POST {spBaseUrl}/api/v1/authorize");
            sb.AppendLine($"Body: {{ \"appSlug\": \"{project.AppSlug ?? project.SchemaName}\", \"resourceIdentifier\": \"...\", \"requiredPermission\": \"read\" }}");
            sb.AppendLine();
            sb.AppendLine("### Refresh Token");
            sb.AppendLine($"POST {spBaseUrl}/api/v1/auth/refresh");
            sb.AppendLine("Body: { \"refreshToken\": \"...\" }");
            sb.AppendLine();
            sb.AppendLine("### Logout");
            sb.AppendLine($"POST {spBaseUrl}/api/v1/auth/logout");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
