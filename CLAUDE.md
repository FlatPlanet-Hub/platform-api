# FlatPlanet Platform API — Claude Code Context

## What This Is

A .NET 10 backend API (HubApi) that acts as a secure proxy for Supabase (PostgreSQL) and GitHub.
It manages projects, project members, Claude Code API token generation, and database migrations
for the FlatPlanet Hub platform.

**HubApi does NOT handle authentication.** Identity and access management is owned by the
standalone `flatplanet-security-platform` service. HubApi validates Security Platform JWTs
and delegates all role/permission checks to it via HTTP.

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
- One repository interface per aggregate (`IProjectRepository`, `IApiTokenRepository`)
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
| `platform.projects` | Project registry — name, schema, GitHub repo, app_slug, app_id, tech_stack, owner |
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

HubApi accepts two token types, both validated against the same JWT secret:

| Token | Issued by | Used for | `token_type` claim | Lifetime |
|---|---|---|---|---|
| Security Platform JWT | flatplanet-security-platform | Frontend ↔ HubApi | none | 60 min |
| HubApi API token | HubApi (`/api/projects/{id}/claude-config`) | Claude Code ↔ DB proxy | `api_token` | 30 days |

`ProjectScopeMiddleware` activates project-scope extraction **only** when BOTH:
1. The route contains a `{projectId}` segment
2. The token has `token_type = "api_token"`

All other requests — including Security Platform JWTs (no `token_type` claim) — pass through unconditionally.

**JWT config (must match Security Platform):**
```json
"Jwt": {
  "Issuer": "flatplanet-security",
  "Audience": "flatplanet-apps",
  "SecretKey": "MUST_MATCH_SECURITY_PLATFORM_SECRET"
}
```

**JWT claims issued by the Security Platform:**
- `sub` — user ID (UUID)
- `email` — user email
- `full_name` — user display name (NOT `name`)
- `company_id` — company UUID
- `session_id` — session UUID
- roles via `ClaimTypes.Role`

---

## Security Platform Integration

HubApi calls the Security Platform for all identity and access operations.

```json
"SecurityPlatform": {
  "BaseUrl": "https://<sp-host>",
  "ServiceToken": "<service-to-service-token>"
}
```

Interface: `ISecurityPlatformService` (`Application/Interfaces/`)
Implementation: `SecurityPlatformService` (`Infrastructure/ExternalServices/`)

Two HTTP clients are used internally:

| Client | Auth | Used for |
|---|---|---|
| `SecurityPlatform` | Service token | All admin calls (register app, grant role, list members, get user) |
| `SecurityPlatformUser` | Forwarded request JWT | `AuthorizeAsync` only — SP derives userId from the bearer token |

`SecurityPlatformService` injects `IHttpContextAccessor` to extract the caller's JWT
for authorization calls. This means `AuthorizeAsync` checks the actual requesting user's
permissions, not the service account's.

Key SP calls made by HubApi:

| When | SP Endpoint | Purpose |
|---|---|---|
| `POST /api/projects` | `POST /api/v1/apps` | Register project as an app, get appId |
| `POST /api/projects` | `POST /api/v1/apps/{appId}/permissions` × 5 | Create project permissions |
| `POST /api/projects` | `POST /api/v1/apps/{appId}/roles` × 3 | Create owner/developer/viewer roles |
| `POST /api/projects` | `POST /api/v1/apps/{appId}/roles/{id}/permissions` × 9 | Assign permissions to roles |
| `POST /api/projects` | `POST /api/v1/apps/{appId}/users` | Grant creator owner role |
| `GET /api/projects` | `GET /api/v1/users/{userId}` → `appAccess[]` | Get user's app roles → filter visible projects |
| Permission check | `POST /api/v1/authorize` | Check read / manage_members / delete_project |
| Member add | `POST /api/v1/apps/{appId}/users` | Grant role (looks up roleId by name first) |
| Member update | `PUT /api/v1/apps/{appId}/users/{userId}/role` | Change role (looks up roleId by name first) |
| Member remove | `DELETE /api/v1/apps/{appId}/users/{userId}` | Revoke role |
| Member list | `GET /api/v1/apps/{appId}/users` | List all members |
| User lookup | `GET /api/v1/users/{userId}` | Get fullName + email for member list response |

**Important:** SP's `POST /api/v1/apps/{appId}/users` requires `roleId` (UUID), not a role name.
`SecurityPlatformService.GrantRoleAsync` resolves this internally by calling
`GET /api/v1/apps/{appId}/roles` first to look up the UUID by name.

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
- Add repo collaborator when a member is invited (only if `GitHubUsername` provided in request)
- Remove repo collaborator when a member is removed (only if `GitHubUsername` was stored)

Role → GitHub permission mapping: `owner` → `admin`, `developer` → `push`, `viewer` → `pull`

Interface: `IGitHubRepoService` (4 methods only):
- `SeedProjectFilesAsync(Project project)`
- `SyncDataDictionaryAsync(Guid projectId, string schema)`
- `InviteCollaboratorAsync(string repo, string githubUsername, string permission)`
- `RemoveCollaboratorAsync(string repo, string githubUsername)`

---

## API Surface

### Auth
- `GET /api/auth/me` — returns identity from JWT claims (no DB call)

### Projects
- `GET /api/projects` — list projects user has access to (via Security Platform)
- `POST /api/projects` — create project (requires `company_id` claim in JWT)
- `GET /api/projects/{id}` — get project
- `PUT /api/projects/{id}` — update project (requires `manage_members` via SP)
- `DELETE /api/projects/{id}` — deactivate project (requires `delete_project` via SP)

### Project Members
- `GET /api/projects/{id}/members` — list members (via SP)
- `POST /api/projects/{id}/members` — add member, body: `{ userId, role, githubUsername? }`
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

| Feature | File |
|---|---|
| F1 | Feature 1 - Supabase Proxy API — Secure Database Access |
| F2 | Feature 2 - GitHub OAuth + JWT Token Issuance |
| F3 | Feature 3 - Admin User Onboarding & Access Management |
| F4 | Feature 4 - GitHub Repository Operations via Proxy API |
| F5 | Feature 5 - CLAUDE.md — Local Project Context File |
| F6 | Feature 6 - Flat Planet IAM — Centralized Identity & Access Management |

---

## Known Limitations

1. **GitHub username not available from Security Platform** — The SP user model has no
   `GitHubUsername` field. GitHub collaborator management only works at invite time when
   the frontend explicitly provides `githubUsername` in the request body. Role changes and
   member removal do not update GitHub repo access. This is a known gap until the SP exposes
   GitHub identity data.

2. **Legacy projects without `AppSlug`** — Projects created before the SP migration have
   `AppSlug = null`. All SP auth checks are skipped for these projects (intentional safety
   net). They must be manually registered with the SP before going to production.

3. **Project setup makes ~19 SP calls on creation** — `SetupProjectRolesAsync` creates
   permissions and roles sequentially. Acceptable now; flag to SP team for a bulk endpoint.

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
- Do not read the `name` JWT claim — the Security Platform issues `full_name`
