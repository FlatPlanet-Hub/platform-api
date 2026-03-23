# FlatPlanet.Platform — Feature 4: GitHub Repository Operations via Proxy API

## What This Is
API endpoints that let Claude (via MCP) perform GitHub operations through your proxy API. The API uses the authenticated user's stored GitHub access token (from OAuth login in Feature 2) to make GitHub API calls server-side. Users never pass GitHub tokens to Claude directly.

## Architecture
```
Claude Desktop → MCP → Your API (JWT auth)
                            ↓
                  Looks up user's encrypted GitHub token
                  from platform.user_oauth_links (Feature 6 IAM)
                            ↓
                  Calls GitHub API on behalf of user
```

The user's GitHub token is stored encrypted in `platform.user_oauth_links.access_token_encrypted`, linked by `provider_id` (GitHub OAuth provider) and `user_id`. This table is managed by Feature 6 IAM — tokens are written during OAuth login (Feature 2) and read here for GitHub API calls. The API decrypts server-side using `IEncryptionService` for each operation.

## Tech Stack
- .NET 10 Web API
- Octokit.net (GitHub API client for .NET) or HttpClient with GitHub REST API
- AES-256 encryption for stored tokens via `IEncryptionService` (Infrastructure layer)

---

## API ENDPOINTS

All endpoints require a valid JWT (app token or api_token). The API resolves the user from the JWT, retrieves their encrypted GitHub token from `platform.user_oauth_links`, and uses it for GitHub operations. Permissions are enforced via Feature 6 IAM (`user_app_roles` + `role_permissions`).

### Repository Management

- `POST /api/projects/{projectId}/repo` — Create GitHub repo when project is created
  ```json
  {
    "repoName": "my-saas-app",
    "description": "Customer management platform",
    "isPrivate": true,
    "org": "FlatPlanet-Hub"
  }
  ```
  → Creates repo under the org (or user's personal account if no org specified), seeds with PROJECT.md, updates `platform.projects.github_repo` with the repo full name

  Response:
  ```json
  {
    "success": true,
    "data": {
      "repoFullName": "FlatPlanet-Hub/my-saas-app",
      "repoUrl": "https://github.com/FlatPlanet-Hub/my-saas-app",
      "cloneUrl": "https://github.com/FlatPlanet-Hub/my-saas-app.git",
      "defaultBranch": "main"
    }
  }
  ```

- `GET /api/projects/{projectId}/repo` — Get repo info
  → Fetches repo details from GitHub using stored repo full name

- `DELETE /api/projects/{projectId}/repo` — Delete repo (owner only, dangerous)
  → Requires confirmation header: `X-Confirm-Delete: true`

### File Operations

- `GET /api/projects/{projectId}/repo/files?path={path}&ref={branch}` — Read file or directory
  
  File response:
  ```json
  {
    "success": true,
    "data": {
      "type": "file",
      "name": "PROJECT.md",
      "path": "PROJECT.md",
      "content": "# My SaaS App\n...",
      "sha": "abc123",
      "size": 1024
    }
  }
  ```

  Directory response:
  ```json
  {
    "success": true,
    "data": {
      "type": "directory",
      "path": "src",
      "items": [
        { "name": "index.ts", "path": "src/index.ts", "type": "file", "size": 512 },
        { "name": "utils", "path": "src/utils", "type": "directory" }
      ]
    }
  }
  ```

- `GET /api/projects/{projectId}/repo/tree?ref={branch}` — Get full file tree
  ```json
  {
    "success": true,
    "data": {
      "sha": "main-sha",
      "tree": [
        { "path": "PROJECT.md", "type": "blob", "size": 1024 },
        { "path": "src/index.ts", "type": "blob", "size": 512 },
        { "path": "src/utils", "type": "tree" }
      ]
    }
  }
  ```

- `PUT /api/projects/{projectId}/repo/files` — Create or update a single file
  ```json
  {
    "path": "src/index.ts",
    "content": "console.log('hello');",
    "message": "Add entry point",
    "branch": "main",
    "sha": "existing-file-sha-if-updating"
  }
  ```
  → `sha` is required when updating an existing file (prevents conflicts). Omit for new files.

- `DELETE /api/projects/{projectId}/repo/files` — Delete a file
  ```json
  {
    "path": "src/old-file.ts",
    "message": "Remove old file",
    "branch": "main",
    "sha": "file-sha"
  }
  ```

### Commit Operations

- `POST /api/projects/{projectId}/repo/commits` — Create a multi-file commit (push multiple files at once)
  ```json
  {
    "message": "feat: add user module",
    "branch": "main",
    "files": [
      {
        "path": "src/models/user.ts",
        "content": "export interface User { ... }",
        "action": "create"
      },
      {
        "path": "src/services/userService.ts",
        "content": "export class UserService { ... }",
        "action": "create"
      },
      {
        "path": "src/old-file.ts",
        "action": "delete"
      }
    ]
  }
  ```
  → Uses Git tree API to create a single commit with multiple file changes. Actions: "create", "update", "delete".

  Response:
  ```json
  {
    "success": true,
    "data": {
      "commitSha": "def456",
      "commitUrl": "https://github.com/FlatPlanet-Hub/my-saas-app/commit/def456",
      "filesChanged": 3
    }
  }
  ```

- `GET /api/projects/{projectId}/repo/commits?branch={branch}&page=1&pageSize=20` — List commits
  ```json
  {
    "success": true,
    "data": [
      {
        "sha": "def456",
        "message": "feat: add user module",
        "author": "Chris-Moriarty",
        "date": "2025-03-19T10:30:00Z"
      }
    ]
  }
  ```

### Branch Operations

- `GET /api/projects/{projectId}/repo/branches` — List branches
  ```json
  {
    "success": true,
    "data": [
      { "name": "main", "isDefault": true, "sha": "abc123" },
      { "name": "feature/user-module", "isDefault": false, "sha": "def456" }
    ]
  }
  ```

- `POST /api/projects/{projectId}/repo/branches` — Create branch
  ```json
  {
    "name": "feature/user-module",
    "fromBranch": "main"
  }
  ```

- `DELETE /api/projects/{projectId}/repo/branches/{branchName}` — Delete branch
  → Cannot delete default branch

### Pull Request Operations

- `POST /api/projects/{projectId}/repo/pulls` — Create PR
  ```json
  {
    "title": "feat: add user module",
    "body": "Adds user model and service",
    "head": "feature/user-module",
    "base": "main"
  }
  ```

- `GET /api/projects/{projectId}/repo/pulls?state=open` — List PRs

- `GET /api/projects/{projectId}/repo/pulls/{prNumber}` — Get PR details

- `PUT /api/projects/{projectId}/repo/pulls/{prNumber}/merge` — Merge PR
  ```json
  {
    "mergeMethod": "squash"
  }
  ```
  → mergeMethod: "merge", "squash", or "rebase"

### Collaborator Management

- `GET /api/projects/{projectId}/repo/collaborators` — List repo collaborators

- `POST /api/projects/{projectId}/repo/collaborators` — Invite collaborator
  ```json
  {
    "githubUsername": "jane-dev",
    "permission": "push"
  }
  ```
  → permission: "pull" (read), "push" (write), "admin"

- `DELETE /api/projects/{projectId}/repo/collaborators/{githubUsername}` — Remove collaborator

---

## REPO CREATION FLOW (when project is created)

When `POST /api/projects/{projectId}/repo` is called:
1. Validate user has access to the project via `user_app_roles` (Feature 6 IAM)
2. Retrieve + decrypt user's GitHub access token from `platform.user_oauth_links` (where `provider_id` = GitHub OAuth provider)
3. Create private repo via GitHub API under specified org
4. Seed with initial files via multi-file commit:
   - `PROJECT.md` — project description, goals, tech stack
   - `DATA_DICTIONARY.md` — empty template, updated as tables are created
   - `.gitignore` — appropriate for the project type
   - `README.md` — basic readme
5. Update `platform.projects.github_repo` with repo full name
6. Log action to `platform.auth_audit_log` via `IAuditService` (Feature 6)
7. Return repo details

---

## DATA_DICTIONARY.md AUTO-SYNC

When tables are created or altered via Feature 1 (migration endpoints), the API should also update `DATA_DICTIONARY.md` in the repo. This keeps the MD file in sync with the actual database schema.

Flow:
1. User creates/alters table via `POST /api/projects/{id}/migration/create-table`
2. Feature 1 executes the DDL
3. Feature 1 calls Feature 4 internally to update `DATA_DICTIONARY.md`
4. Feature 4 fetches current schema from `information_schema`, generates updated MD, commits to repo

---

## SECURITY REQUIREMENTS
1. **Token encryption** — GitHub access tokens encrypted with AES-256 at rest in `platform.user_oauth_links.access_token_encrypted`, decrypted server-side only when making API calls via `IEncryptionService`
2. **Project scoping** — User can only perform repo operations on projects they have an active role for via `platform.user_app_roles` (Feature 6 IAM). No `project_members` dependency.
3. **Permission mapping** — IAM role permissions (via `role_permissions` + `permissions` tables) map to GitHub operations:
   - `read` → can read files, list branches/commits/PRs
   - `write` → can push files, create commits
   - `ddl` → can create repos, manage branches
   - `manage_members` → can add/remove collaborators
4. **Audit logging** — Log all repo creates, commits, PR merges, collaborator changes to `platform.auth_audit_log` via `IAuditService`
5. **Rate limiting** — GitHub API has rate limits (5000 req/hr for authenticated users). Implement caching and respect rate limit headers.
6. **File size limits** — GitHub API has a 100MB file limit. Reject files over 50MB as a safety margin.
7. **Branch protection** — Default branch (main) should not allow direct deletion

---

## RESPONSE FORMAT
```json
{
  "success": true,
  "data": { ... },
  "error": null
}
```
