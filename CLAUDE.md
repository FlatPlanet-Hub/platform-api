# FlatPlanet Platform API — Claude Code Context

## What This Is

A .NET 10 backend API (HubApi) that acts as a secure proxy for Supabase (PostgreSQL) and GitHub. It manages projects, project members, Claude Code API token generation, and database migrations for the FlatPlanet Hub platform.

**HubApi does NOT handle authentication.** Identity and access management is owned by the standalone `flatplanet-security-platform` service. HubApi validates Security Platform JWTs and delegates all role/permission checks to it via HTTP.

---

## Solution Structure

```
FlatPlanet.Platform.API/           # Presentation — controllers, middleware, filters
FlatPlanet.Platform.Application/   # Business logic — services, interfaces, DTOs
FlatPlanet.Platform.Domain/        # Entities, enums, value objects (no dependencies)
FlatPlanet.Platform.Infrastructure/# Repositories, external services, config
FlatPlanet.Platform.Tests/         # xUnit unit tests
db/migrations/                     # SQL migration files (run manually against Supabase)
Features/                          # Feature specs — read these before implementing anything
```

---

## Architecture Rules

**Layer flow:** Controller → Application Service → Domain → Infrastructure

- No business logic in controllers — controllers only parse input, call a service, return a response
- No direct DB access from the API layer
- Do not skip layers
- Dependencies are one-directional — outer layers depend on inner layers, never the reverse
- All cross-layer contracts defined as interfaces in Application layer

**Repository rules:**
- One repository interface per aggregate (e.g. `IProjectRepository`)
- No generic repositories
- Only simple domain queries and persistence methods

---

## Tech Stack

- .NET 10 + ASP.NET Core Web API
- PostgreSQL via Supabase (connection pooler port 6543)
- Dapper for all DB access — no EF Core
- Octokit.net for GitHub API (service token only — no per-user OAuth)
- JWT Bearer authentication — validates tokens issued by Security Platform
- xUnit + Moq for tests

---

## Database

HubApi owns only two tables:

| Table | Purpose |
|---|---|
| `platform.projects` | Project registry — name, schema, GitHub repo, app_slug, app_id, owner |
| `platform.api_tokens` | Claude Code API tokens — scoped to a project, stored as SHA-256 hash |

Everything else (users, roles, sessions, audit, OAuth) lives in the Security Platform.

- Schema prefix: `platform.` for HubApi tables
- Project data schemas: `project_{slug}` (isolated per project)
- All queries use parameterized Dapper — never string-concatenated SQL
- Migrations live in `db/migrations/` — run manually, the API does not auto-migrate
- Connection via `IDbConnectionFactory` — never instantiate `NpgsqlConnection` directly
- Schema names validated via `SqlValidationHelper.IsValidSchemaName` before use

---

## Authentication Model

HubApi accepts two token types:

| Token | Issued by | Used for | `token_type` claim | Lifetime |
|---|---|---|---|---|
| Security Platform JWT | flatplanet-security-platform | Frontend ↔ HubApi (projects, members, CLAUDE.md) | none / `app` | 60 min |
| HubApi API token | HubApi (`/api/projects/{id}/claude-config`) | Claude Code ↔ DB proxy | `api_token` | 30 days |

`ProjectScopeMiddleware` activates project-scope extraction **only** when BOTH:
1. The route contains a `{projectId}` segment
2. The token has `token_type = "api_token"`

All other requests — including Security Platform JWTs — pass through unconditionally.

**JWT config (must match Security Platform):**
```json
"Jwt": {
  "Issuer": "flatplanet-security",
  "Audience": "flatplanet-apps",
  "SecretKey": "MUST_MATCH_SECURITY_PLATFORM_SECRET"
}
```

---

## Security Platform Integration

HubApi calls the Security Platform for all identity and access operations. Configure via:

```json
"SecurityPlatform": {
  "BaseUrl": "https://security.flatplanet.com",
  "ServiceToken": "platform-to-platform-service-token"
}
```

Key calls made by HubApi:

| When | Endpoint | Purpose |
|---|---|---|
| `POST /api/projects` | `POST {SP}/api/v1/apps` | Register project as an app |
| `POST /api/projects` | `POST {SP}/api/v1/apps/{appId}/users` | Grant creator `owner` role |
| `GET /api/projects` | `GET {SP}/api/v1/user-context` | Get user's app roles → filter visible projects |
| Member add | `POST {SP}/api/v1/apps/{appId}/users` | Grant role |
| Member remove | Revoke role endpoint | Revoke role |
| Permission check | `POST {SP}/api/v1/authorize` | Check `read` / `manage_members` / `delete_project` |
| Member lookup | `GET {SP}/api/v1/users/{userId}` | Get GitHub username for collaborator management |
| Member list | `GET {SP}/api/v1/apps/{appId}/users` | List all members of a project |

Interface: `ISecurityPlatformService` (`Application/Interfaces/`)
Implementation: `SecurityPlatformService` (`Infrastructure/ExternalServices/`)

---

## GitHub Integration

HubApi uses a **single service token** for all GitHub operations — no per-user OAuth.

```json
"GitHub": {
  "ServiceToken": "ghp_...",
  "OrgName": "FlatPlanet-Hub"
}
```

GitHub operations:
- Seed `DATA_DICTIONARY.md` + `.gitignore` on project creation (fire-and-forget)
- Sync `DATA_DICTIONARY.md` after every DDL operation (fire-and-forget)
- Add/remove repo collaborators when members are added/removed

Role → GitHub permission mapping: `owner` → `admin`, `developer` → `push`, `viewer` → `pull`

Interface: `IGitHubRepoService` (4 methods only — no repo/file/commit/branch/PR operations)

---

## API Surface

### Auth
- `GET /api/auth/me` — returns identity from JWT claims (no DB call)

### Projects
- `GET /api/projects` — list projects user has access to (via Security Platform)
- `POST /api/projects` — create project (requires `create_project` permission in JWT)
- `GET /api/projects/{id}` — get project
- `PUT /api/projects/{id}` — update project (requires `manage_members` via Security Platform)
- `DELETE /api/projects/{id}` — deactivate project (requires `delete_project` via Security Platform)

### Project Members
- `GET /api/projects/{id}/members` — list members (via Security Platform)
- `POST /api/projects/{id}/members` — add member, body: `{ userId, role }`
- `PUT /api/projects/{id}/members/{userId}/role` — change role
- `DELETE /api/projects/{id}/members/{userId}` — remove member + revoke API tokens

### Claude Config (Security Platform JWT required)
- `GET /api/projects/{id}/claude-config` — generate CLAUDE.md + 30-day API token
- `POST /api/projects/{id}/claude-config/regenerate` — revoke + regenerate
- `DELETE /api/projects/{id}/claude-config` — revoke token

### DB Proxy (HubApi API token required)
All routes scoped under `/api/projects/{projectId}/`:
- `GET schema/tables`, `schema/columns`, `schema/relationships`, `schema/full` — requires `read`
- `POST migration/create-table`, `PUT migration/alter-table`, `DELETE migration/drop-table` — requires `ddl`
- `POST query/read` — requires `read`
- `POST query/write` — requires `write`

---

## Feature Specs

All features are documented in `Features/`. **Read the relevant spec before implementing or modifying anything in that area.**

| Feature | File | Status |
|---|---|---|
| F1 | Feature 1 - Supabase Proxy API — Secure Database Access | Done |
| F2 | Feature 2 - Authentication via Security Platform | Done |
| F3 | Feature 3 - Projects & Member Management | Done |
| F4 | Feature 4 - GitHub DATA_DICTIONARY Sync | Done |
| F5 | Feature 5 - CLAUDE.md — Local Project Context File | Done |
| F6 | Feature 6 - Security Platform Integration | Done |

---

## Known Issues

### Open

1. **`GetMembersAsync` uses `DateTime.UtcNow` for `GrantedAt`** — should come from Security Platform `user_app_roles` grant date. `AppMemberDto` needs a `GrantedAt` field populated from the SP response. `ProjectMemberService.cs:51`.

2. **`GET /api/projects/{id}` returns `RoleName: null`** — `GetProjectAsync` calls `ToResponse(project)` without looking up the user's role. Inconsistent with the list response which includes role. `ProjectService.cs:88`.

3. **Projects without `AppSlug` bypass Security Platform auth** — old projects not registered with SP skip all permission checks. Intentional migration safety net, but unprotected. Document before going to production. `ProjectService.cs:83`, `ProjectMemberService.cs:121`.

4. **Audit calls remain in `ClaudeConfigService`** — plan said to remove `_audit.LogAsync` from services; ClaudeConfigService still has 3 calls (`claude_config.generated`, `claude_config.revoked_all`, `claude_config.revoked`). Decision needed: are API token lifecycle events HubApi-local audit (keep) or Security Platform audit (remove)?

### Fixed (Security Platform migration — current batch)

- ~~Routes flat (`/api/schema/`, `/api/migration/`, `/api/query/`)~~ → nested under `/api/projects/{projectId}/`
- ~~`ProjectScopeMiddleware` 403'd all Security Platform JWT users~~ → dual-condition guard
- ~~Full GitHub OAuth, JWT issuance, refresh tokens, sessions in HubApi~~ → removed, owned by Security Platform
- ~~12 dead controllers (Admin, Companies, Apps, Authorize, Audit, Compliance, Resources, Roles, Repo, Token, etc.)~~ → deleted
- ~~8 dead services (AdminUser, AdminRole, App, Company, IamAuthorization, ProjectRole, Resource, User)~~ → deleted
- ~~`ProjectService` used local `project_roles`/`project_members` for auth~~ → Security Platform calls
- ~~`ProjectMemberService` never called GitHub or Security Platform~~ → both wired
- ~~`ClaudeConfigService` queried local users/user_app_roles/role_permissions~~ → Security Platform
- ~~`IJwtService.GenerateApiToken` required `User` entity~~ → accepts `userId`, `userName`, `userEmail`
- ~~`InviteUserRequest` used `GitHubUsername`~~ → uses `UserId`
- ~~`RepoController` exposed repo/file/commit/branch/PR operations~~ → deleted (F4 trimmed to DATA_DICTIONARY sync only)

---

## Coding Conventions

- `async`/`await` for all I/O
- Depend on interfaces, not implementations — always inject via constructor
- Method length: keep under ~50 lines; extract if growing
- DTO naming: `CreateXRequest`, `UpdateXRequest`, `XResponse`, `XDto`
- Domain entity naming: pure noun (`Project`) — no `Entity` suffix unless conflict
- Tests: `MethodName_ShouldDoX_WhenCondition` naming, Arrange/Act/Assert layout
- Commit messages: `feat:`, `fix:`, `refactor:`, `docs:`, `chore:`

---

## Running the Project

```bash
dotnet build FlatPlanet.Platform.slnx
dotnet run --project FlatPlanet.Platform.API
dotnet test FlatPlanet.Platform.Tests
```

API runs on `https://localhost:7xxx` (see `launchSettings.json`).
Scalar API docs available at `/scalar` in Development.

---

## What NOT to Do

- Do not add business logic to controllers
- Do not call repositories directly from the API layer
- Do not concatenate user input into SQL strings
- Do not store raw tokens or secrets — always hash or encrypt
- Do not commit `CLAUDE.md` files generated for end users (they are gitignored)
- Do not use EF Core — this project uses Dapper throughout
- Do not create a generic `IRepository<T>` — use aggregate-specific repositories
- Do not handle authentication in HubApi — all identity is owned by the Security Platform
- Do not use per-user GitHub tokens — all GitHub operations use the service token
