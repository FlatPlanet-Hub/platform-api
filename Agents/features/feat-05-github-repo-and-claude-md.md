# FEAT-05 — GitHub Repo Creation & CLAUDE.md Push on Project Create

**Repo:** FlatPlanetHubApi — `https://github.com/FlatPlanet-Hub/platform-api`
**Local path:** `C:\Users\Erick\source\ClaudeCode\FlatPlanetHubApi`
**Branch:** `feature/feat-05-github-repo-claude-md` (branch from `main`)
**Target:** `main` — sync back to `develop` after merge

---

## Goal

When a project is created:
1. Create a new GitHub repo (or link an existing one)
2. Store full repo metadata on the project (`repoName`, `repoFullName`, `branch`, `repoLink`)
3. Generate `CLAUDE.md` with the project owner's API token and push it to the repo
4. Seed `DATA_DICTIONARY.md` is **removed** — CLAUDE.md handles data dictionary via live Supabase query
5. Project response includes a nested `gitHub` object with all repo fields

---

## DB Migration

File: `db/migrations/009_project_github_fields.sql`

```sql
ALTER TABLE platform.projects
    ADD COLUMN IF NOT EXISTS github_repo_name   TEXT,
    ADD COLUMN IF NOT EXISTS github_branch      TEXT NOT NULL DEFAULT 'main',
    ADD COLUMN IF NOT EXISTS github_repo_link   TEXT;
```

> `github_repo` (existing column) stays — holds `owner/repo` format used by Octokit internally.
> `github_repo_name` holds just the repo name (e.g. `Payzali-test-project-3`).
> `github_repo_link` holds the full GitHub URL (e.g. `https://github.com/FlatPlanet-Hub/Payzali-test-project-3`).

---

## Files to Modify

### 1. `Project.cs` (Domain Entity)

Add 3 new properties:

```csharp
public string? GitHubRepoName   { get; set; }
public string  GitHubBranch     { get; set; } = "main";
public string? GitHubRepoLink   { get; set; }
```

---

### 2. `CreateProjectRequest.cs`

Add optional `GitHub` object:

```csharp
public sealed class CreateProjectRequest
{
    public string  Name        { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? TechStack   { get; init; }
    public GitHubRepoRequest? GitHub { get; init; }
}

public sealed class GitHubRepoRequest
{
    public bool    CreateRepo           { get; init; } = false;
    public string? RepoName            { get; init; }   // required when CreateRepo = true
    public string? ExistingRepoUrl     { get; init; }   // required when CreateRepo = false, e.g. https://github.com/Org/repo
}
```

**Validation rules:**
- `CreateRepo = true` → `RepoName` must be provided
- `CreateRepo = false` → `ExistingRepoUrl` must be provided and must be a valid GitHub URL
- `GitHub = null` → project created with no repo (allowed)

---

### 3. `ProjectResponse.cs`

Replace flat `GitHubRepo` with nested `GitHub` object:

```csharp
public sealed class ProjectResponse
{
    public Guid     Id          { get; init; }
    public string   Name        { get; init; } = string.Empty;
    public string?  Description { get; init; }
    public string   SchemaName  { get; init; } = string.Empty;
    public Guid     OwnerId     { get; init; }
    public string?  AppSlug     { get; init; }
    public string?  RoleName    { get; init; }
    public string?  TechStack   { get; init; }
    public bool     IsActive    { get; init; }
    public DateTime CreatedAt   { get; init; }
    public GitHubRepoResponse?        GitHub  { get; init; }
    public IEnumerable<ProjectMemberResponse>? Members { get; init; }
}

public sealed class GitHubRepoResponse
{
    public string  RepoName     { get; init; } = string.Empty;
    public string  RepoFullName { get; init; } = string.Empty;
    public string  Branch       { get; init; } = "main";
    public string  RepoLink     { get; init; } = string.Empty;
}
```

---

### 4. `IGitHubRepoService.cs`

Add repo creation method:

```csharp
/// <summary>
/// Creates a new GitHub repo under the configured org and returns its full name and URL.
/// </summary>
Task<(string RepoFullName, string RepoLink)> CreateRepoAsync(string repoName);
```

Remove `SeedProjectFilesAsync` — replaced by `PushClaudeMdAsync` below.

Add:

```csharp
/// <summary>
/// Generates and pushes CLAUDE.md to the repo. Creates or updates the file.
/// </summary>
Task PushClaudeMdAsync(string repoFullName, string branch, string content);
```

---

### 5. `GitHubRepoService.cs`

**Add `CreateRepoAsync`:**

```csharp
public async Task<(string RepoFullName, string RepoLink)> CreateRepoAsync(string repoName)
{
    var client = GetServiceClient();
    var repo = await client.Repository.Create(_settings.OrgName, new NewRepository(repoName)
    {
        Private     = true,
        AutoInit    = true,   // creates main branch with initial commit
        Description = $"FlatPlanet project: {repoName}"
    });
    return ($"{_settings.OrgName}/{repoName}", repo.HtmlUrl);
}
```

**Add `PushClaudeMdAsync`:**

```csharp
public async Task PushClaudeMdAsync(string repoFullName, string branch, string content)
{
    var client = GetServiceClient();
    var (owner, repoName) = ParseRepo(repoFullName);
    var encodedContent = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));

    try
    {
        var existing = await client.Repository.Content.GetAllContents(owner, repoName, "CLAUDE.md");
        var sha = existing.FirstOrDefault()?.Sha;
        await client.Repository.Content.UpdateFile(owner, repoName, "CLAUDE.md",
            new UpdateFileRequest("chore: update CLAUDE.md", content, sha, branch));
    }
    catch (NotFoundException)
    {
        await client.Repository.Content.CreateFile(owner, repoName, "CLAUDE.md",
            new CreateFileRequest("chore: add CLAUDE.md", content, branch));
    }
}
```

**Remove `DATA_DICTIONARY.md` from `SeedProjectFilesAsync`** — only `.gitignore` is seeded.

**Update `.gitignore`** — remove `CLAUDE.md` from the gitignore list (it must now be committed):

```csharp
private static string BuildGitignore() => """
    # Dependencies
    node_modules/
    .pnp
    .pnp.js

    # Build outputs
    dist/
    build/
    .next/
    out/

    # Environment variables
    .env
    .env.local
    .env.*.local

    # IDE
    .vscode/
    .idea/
    *.suo
    *.user

    # OS
    .DS_Store
    Thumbs.db

    # Logs
    *.log
    npm-debug.log*
    """;
```

---

### 6. `ProjectService.CreateProjectAsync`

Full updated flow:

```csharp
public async Task<ProjectResponse> CreateProjectAsync(
    Guid userId, string actorEmail, Guid companyId, string baseUrl,
    CreateProjectRequest request, string? ipAddress)
{
    var shortId    = Guid.NewGuid().ToString("N")[..8];
    var schemaName = $"project_{shortId}";
    var appSlug    = GenerateSlug(request.Name);

    // 1. Resolve GitHub repo info
    string? repoFullName = null;
    string? repoName     = null;
    string? repoLink     = null;
    const string branch  = "main";

    if (request.GitHub is not null)
    {
        if (request.GitHub.CreateRepo)
        {
            if (string.IsNullOrWhiteSpace(request.GitHub.RepoName))
                throw new ArgumentException("RepoName is required when CreateRepo is true.");

            (repoFullName, repoLink) = await _gitHubRepo.CreateRepoAsync(request.GitHub.RepoName);
            repoName = request.GitHub.RepoName;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.GitHub.ExistingRepoUrl))
                throw new ArgumentException("ExistingRepoUrl is required when CreateRepo is false.");

            repoLink     = request.GitHub.ExistingRepoUrl.TrimEnd('/');
            var uri      = new Uri(repoLink);
            repoFullName = uri.AbsolutePath.TrimStart('/');          // e.g. "FlatPlanet-Hub/my-repo"
            repoName     = repoFullName.Split('/').Last();
        }
    }

    // 2. Register with SP first — if this fails, nothing is persisted
    var appId = await _securityPlatform.RegisterAppAsync(request.Name, appSlug, baseUrl, companyId);
    await _securityPlatform.SetupProjectRolesAsync(appId);
    await _securityPlatform.GrantRoleAsync(appId, userId, "owner");

    // 3. Insert DB row after SP succeeds
    var project = new Project
    {
        Id              = Guid.NewGuid(),
        Name            = request.Name,
        Description     = request.Description,
        SchemaName      = schemaName,
        AppId           = appId,
        AppSlug         = appSlug,
        OwnerId         = userId,
        TechStack       = request.TechStack,
        GitHubRepo      = repoFullName,
        GitHubRepoName  = repoName,
        GitHubBranch    = branch,
        GitHubRepoLink  = repoLink,
        IsActive        = true,
        CreatedAt       = DateTime.UtcNow,
        UpdatedAt       = DateTime.UtcNow
    };

    var created = await _projectRepo.CreateAsync(project);

    // 4. Seed repo files + generate and push CLAUDE.md
    if (repoFullName is not null)
    {
        _ = _gitHubRepo.SeedProjectFilesAsync(created);

        var claudeMd = _claudeConfig.RenderTemplate(created, userId, actorEmail, baseUrl);
        _ = _gitHubRepo.PushClaudeMdAsync(repoFullName, branch, claudeMd);
    }

    // 5. Create project schema in Supabase
    _ = _dbProxy.CreateSchemaAsync(schemaName);

    await _auditLog.LogAsync(userId, actorEmail, AuditAction.ProjectCreate,
        "project", created.Id, new { name = created.Name, appSlug }, ipAddress);

    return ToResponse(created, "owner");
}
```

---

### 7. `ClaudeConfigService.cs`

Extract `RenderTemplate` to be callable from `ProjectService`:

- Change `private static string RenderTemplate(...)` to `public string RenderTemplate(...)`
- Update signature to accept `userId` and `actorEmail` so it can generate and store the token:

```csharp
public async Task<string> RenderAndStoreTokenAsync(
    Project project, Guid userId, string userName, string userEmail, string baseUrl)
```

This method:
1. Generates the API token (existing logic)
2. Stores it in `api_tokens`
3. Returns the rendered `CLAUDE.md` string

> `ProjectService` calls this and passes the result to `PushClaudeMdAsync`.
> The existing `GenerateAsync` endpoint on the controller still works — it calls the same method and also pushes the updated `CLAUDE.md` to the repo.

---

### 8. `ProjectRepository.cs`

Ensure `CreateAsync` and `UpdateAsync` include the 3 new columns:
- `github_repo_name`
- `github_branch`
- `github_repo_link`

And `SELECT *` queries will map them automatically via `MatchNamesWithUnderscores = true`.

---

### 9. `ProjectService.ToResponse`

Update to populate the nested `GitHub` object:

```csharp
private static ProjectResponse ToResponse(Project p, string? roleName = null) => new()
{
    Id          = p.Id,
    Name        = p.Name,
    Description = p.Description,
    SchemaName  = p.SchemaName,
    OwnerId     = p.OwnerId,
    AppSlug     = p.AppSlug,
    TechStack   = p.TechStack,
    IsActive    = p.IsActive,
    CreatedAt   = p.CreatedAt,
    RoleName    = roleName,
    GitHub      = p.GitHubRepo is null ? null : new GitHubRepoResponse
    {
        RepoName     = p.GitHubRepoName ?? string.Empty,
        RepoFullName = p.GitHubRepo,
        Branch       = p.GitHubBranch,
        RepoLink     = p.GitHubRepoLink ?? string.Empty
    }
};
```

---

## Wire-up

No new DI registrations needed. `ProjectService` already injects `IGitHubRepoService`, `IDbProxyService`, `ISecurityPlatformService`, `IAuditLogRepository`. Add `IClaudeConfigService` to the constructor.

---

## Expected Response on Project Create

```json
{
  "success": true,
  "data": {
    "id": "f56c5da7-...",
    "name": "Payzali Test Project 3",
    "schemaName": "project_abc12345",
    "appSlug": "payzali-test-project-3",
    "techStack": "Next.js",
    "isActive": true,
    "createdAt": "2026-04-01T00:00:00Z",
    "roleName": "owner",
    "gitHub": {
      "repoName": "Payzali-test-project-3",
      "repoFullName": "FlatPlanet-Hub/Payzali-test-project-3",
      "branch": "main",
      "repoLink": "https://github.com/FlatPlanet-Hub/Payzali-test-project-3"
    }
  }
}
```

---

## Testing After Deploy

1. `POST /api/projects` with `github.createRepo = true` → repo appears in GitHub org, `CLAUDE.md` is pushed
2. `POST /api/projects` with `github.existingRepoUrl` → project linked, `CLAUDE.md` pushed to existing repo
3. `POST /api/projects` with `github = null` → project created, no GitHub actions
4. `GET /api/projects/{id}` → response includes `gitHub` object with all 4 fields
5. Clone the repo → `CLAUDE.md` is present and contains valid token + all instructions
6. Regenerate CLAUDE.md from Hub → updated file is pushed to repo automatically
