# FEAT-05 — Coder Instructions
# GitHub Repo Creation & CLAUDE.md Push on Project Create

---

## Before You Start

- **Repo:** `https://github.com/FlatPlanet-Hub/platform-api`
- **Local path:** `C:\Users\Erick\source\ClaudeCode\FlatPlanetHubApi`
- **Branch from:** `main`
- **Branch name:** `feature/feat-05-github-repo-claude-md`
- **PR targets:** `main`
- **Full spec:** `Agents/features/feat-05-github-repo-and-claude-md.md` — read it fully before writing any code
- **Architecture rules:** `CLAUDE.md` at the repo root — follow these at all times

---

## Step 1 — Create your branch

```bash
git checkout main
git pull origin main
git checkout -b feature/feat-05-github-repo-claude-md
```

---

## Step 2 — DB Migration

Create file `db/migrations/009_project_github_fields.sql`:

```sql
ALTER TABLE platform.projects
    ADD COLUMN IF NOT EXISTS github_repo_name   TEXT,
    ADD COLUMN IF NOT EXISTS github_branch      TEXT NOT NULL DEFAULT 'main',
    ADD COLUMN IF NOT EXISTS github_repo_link   TEXT;
```

> Do NOT run this yet — the reviewer will confirm when to apply it.

---

## Step 3 — Implementation Order

Follow this order exactly. Each step must compile before moving to the next.

1. **`Project.cs`** — add `GitHubRepoName`, `GitHubBranch`, `GitHubRepoLink` properties
2. **`CreateProjectRequest.cs`** — add `GitHubRepoRequest` nested class and `GitHub` property
3. **`GitHubRepoResponse.cs`** — new DTO (`RepoName`, `RepoFullName`, `Branch`, `RepoLink`)
4. **`ProjectResponse.cs`** — replace flat `GitHubRepo` with `GitHub` of type `GitHubRepoResponse?`
5. **`IGitHubRepoService.cs`** — add `CreateRepoAsync`, add `PushClaudeMdAsync`
6. **`GitHubRepoService.cs`** — implement both new methods, remove `DATA_DICTIONARY.md` from seed, remove `CLAUDE.md` from `.gitignore`
7. **`ClaudeConfigService.cs`** — extract render+store into `public async Task<string> RenderAndStoreTokenAsync(...)` callable from ProjectService
8. **`IClaudeConfigService.cs`** — add `RenderAndStoreTokenAsync` to interface
9. **`ProjectService.cs`** — update `CreateProjectAsync` per spec, inject `IClaudeConfigService`, update `ToResponse` for nested `GitHub` object
10. **`ProjectRepository.cs`** — include 3 new columns in `CreateAsync` INSERT
11. **`ProjectController.cs`** — no changes needed (signature already passes actorEmail and ip)

---

## Step 4 — Key Rules to Follow

- **SP registration happens BEFORE the DB insert** — this is already the case after BUG-04. Do not revert this order.
- **`CreateRepoAsync` is called BEFORE SP registration** — if GitHub fails, nothing is persisted
- **`PushClaudeMdAsync` and `SeedProjectFilesAsync` run fire-and-forget** (`_ = ...`) — do not await them in the main flow
- **`RenderAndStoreTokenAsync` must be awaited** — it persists the token to DB
- **If `request.GitHub` is null** — skip all GitHub steps, project is created without a repo
- **Validation:**
  - `createRepo: true` + no `repoName` → throw `ArgumentException`
  - `createRepo: false` + no `existingRepoUrl` → throw `ArgumentException`
  - `existingRepoUrl` must be parseable as a URI with at least 2 path segments (`owner/repo`)

---

## Step 5 — Tests

Update `FlatPlanet.Platform.Tests/UnitTests/Services/ProjectServiceTests.cs`:

- Add mock for `IClaudeConfigService`
- Update `CreateProject_ShouldProvisionSchema_AndRegisterApp` to pass `github: null` (no repo)
- Add new test: `CreateProject_WithCreateRepo_ShouldCallCreateRepoAsync`
- Add new test: `CreateProject_WithExistingRepo_ShouldParseRepoUrl`
- Add new test: `CreateProject_WithNoGitHub_ShouldSkipGitHubCalls`

---

## Step 6 — Open PR

```bash
git push origin feature/feat-05-github-repo-claude-md
```

Open PR against `main` on `https://github.com/FlatPlanet-Hub/platform-api`.

Title: `feat: FEAT-05 — GitHub repo creation and CLAUDE.md push on project create`

Tag reviewer when ready.

---

## What NOT to do

- Do not run the migration — leave that for the reviewer to confirm
- Do not change `ClaudeConfigController` — existing endpoints still work as-is
- Do not remove `SyncDataDictionaryAsync` — it is still used for schema sync after table changes
- Do not await fire-and-forget calls (`SeedProjectFilesAsync`, `PushClaudeMdAsync`)
- Do not add `CLAUDE.md` back to `.gitignore`
- Do not target `develop` — this PR goes to `main`
