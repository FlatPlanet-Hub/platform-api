# Changelog

All notable changes to this project are documented here.
Versioning follows [Semantic Versioning](https://semver.org/) — `MAJOR.MINOR.PATCH`.

---

## [0.8.4] — 2026-03-27

### Changed
- **Response envelope** — `rowsAffected` and `error` are now omitted from the JSON response when null (`JsonIgnore(WhenWritingNull)`). `WhenWritingNull` applied globally via `AddJsonOptions`. Void endpoints return `{ "success": true }` only — no `"data": null`.
- **JWT claim mapping disabled** — `MapInboundClaims = false` set on JWT Bearer options. Claims are now read exactly as issued (`sub`, `email`, `full_name`, etc.). ASP.NET's default mapping that renames `sub` → `NameIdentifier` is no longer active.
- **Renamed** `docs/api-reference.md` → `docs/platform-api-reference.md`
- **`UserSecretsId`** added to `FlatPlanet.Platform.API.csproj` — use `dotnet user-secrets` for local dev credentials

### Fixed
- **`GET /api/projects` — empty list for new users** — `GetUserAppAccessAsync` now handles SP `404` (user not yet registered) gracefully, returning an empty list. Previously threw an unhandled exception.
- **`GET /api/projects` — empty `appIds` crash** — added early return when the user has no app access, preventing `GetByAppIdsAsync(empty list)` from reaching the DB.
- **Npgsql connection pool** — tuned for Supabase PgBouncer compatibility: disabled keepalive, disabled reset-on-close, reduced pool size. Prevents stale connection errors on idle periods.

### Database
- Migration `007_drop_stale_api_tokens_fks.sql` — drops FK constraints on `platform.api_tokens` that referenced `users` and `apps` tables which no longer exist in HubApi's DB (they live in the Security Platform).

### Docs
- Updated `docs/platform-api-reference.md` [0.8.4]: null fields omitted from all response examples, added `MapInboundClaims` note to auth overview, added new-user SP 404 handling note to List Projects, updated Standard Response Envelope section
- Updated `README.md`: doc link updated to new filename, added user secrets guidance

---

## [0.8.3] — 2026-03-27

### Added
- **`view_all_projects` admin permission** — users with this permission on the `dashboard-hub` app now see all projects in `GET /api/projects`, bypassing the normal SP membership filter. Their `roleName` is `"admin"` for projects they are not explicitly a member of.

### Fixed
- **`GET /api/projects/{id}` admin bypass** — `GetProjectAsync` now skips the SP `authorize` check for `view_all_projects` users, consistent with the list endpoint. Previously, they would receive `403` even though `GET /api/projects` showed the project.

### Docs
- Updated `docs/api-reference.md` [0.8.3]: documented admin override behavior on List Projects and Get Project endpoints

---

## [0.8.2] — 2026-03-27

### Docs
- **Corrected error codes in `docs/api-reference.md`** based on `GlobalExceptionMiddleware` mapping
  - `InvalidOperationException` → `409` (not `422`) — affects all Claude Config endpoints when project has no `appSlug`
  - Postgres execution errors (syntax, constraint violations, `SetNotNull` on nullable data) → `500` (unhandled, not `422`)
  - Removed `422` entirely from the error reference table — it has no middleware mapping
  - Added `GlobalExceptionMiddleware` exception-to-HTTP-code mapping table to the Error Reference section
  - Added `500` guidance note for query/migration failures
- Bumped reference version to 0.8.2

---

## [0.8.1] — 2026-03-27

### Docs
- **Rewrote `docs/api-reference.md`** — complete frontend integration reference reflecting the current v0.8.0 API surface after the Security Platform migration
  - Added API Tokens section (`POST/GET/DELETE /api/auth/api-tokens`) — previously undocumented
  - Added `POST /migration/create-schema` endpoint — previously missing from the reference
  - Corrected `ColumnDefinition` payload shape: fields are `name`, `type`, `nullable`, `isPrimaryKey`, `default` (prior doc incorrectly used `columnName`, `dataType`, `defaultValue`)
  - Corrected all endpoint response codes from `204` to `200` to match actual controller behavior
  - Removed stale sections: GitHub OAuth flow, admin endpoints, legacy IAM tables — all now owned by `flatplanet-security-platform`
  - Added realistic request/response examples for every endpoint
  - Added `mcpConfig` shape to API token creation response
  - Added Security Platform dependency callouts (`502` behavior) and known limitation notes

---

## [0.5.0] — 2026-03-23
> Branch: `feature/github-repo-operations`

### Changed — Project Rename & Architecture Refactor
- **Renamed** entire solution from `SupabaseProxy` → `FlatPlanet.Platform` (Company.Product .NET convention)
  - 5 project folders, 5 .csproj files, solution file, 112 C# files (namespaces + usings)
  - OpenAPI title updated to "FlatPlanet Platform API"
  - README updated to reflect platform identity
- **Moved services to Application layer** — `AdminRoleService`, `AdminUserService`, `UserService`, `ProjectService` relocated from Infrastructure to Application (clean architecture compliance)
- **Split fat repositories** — `IProjectRepository` split into `IProjectRepository`, `IProjectRoleRepository`, `IProjectMemberRepository` (Interface Segregation Principle)
- **Split ProjectService** — extracted `ProjectRoleService` and `ProjectMemberService` (Single Responsibility)
- **Added `IDbConnectionFactory`** — centralized DB connection creation, injected into all repositories
- **Added `IEncryptionService`** — abstracted encryption behind an Application-layer interface
- **Added `ApiControllerBase`** — extracted `GetUserId()` into shared base controller
- **Added `GlobalExceptionMiddleware`** — centralized error handling with structured logging
- **Added Feature 5: Claude Config** — `ClaudeConfigService`, `ClaudeConfigController`, `IClaudeConfigService`
- 76 unit tests passing (7 new)

---

## [0.4.0] — 2026-03-19
> Branch: `feature/github-repo-operations` → `develop`
> Commit: `4e28a56`

### Added — Feature 4: GitHub Repository Operations via Proxy API
- `POST /api/projects/{id}/repo` — create GitHub repo under org or personal account; seeds `PROJECT.md`, `DATA_DICTIONARY.md`, `README.md`, `.gitignore` as initial commit
- `GET /api/projects/{id}/repo` — fetch live repo details from GitHub
- `DELETE /api/projects/{id}/repo` — delete repo (requires `X-Confirm-Delete: true` header); org permission failure returns 403 with admin escalation message
- `GET /api/projects/{id}/repo/files?path=&ref=` — read file (decoded) or directory listing
- `GET /api/projects/{id}/repo/tree?ref=` — recursive file tree
- `PUT /api/projects/{id}/repo/files` — create or update a single file
- `DELETE /api/projects/{id}/repo/files` — delete a file by SHA
- `POST /api/projects/{id}/repo/commits` — multi-file atomic commit via Git tree API (create / update / delete actions)
- `GET /api/projects/{id}/repo/commits?branch=&page=&pageSize=` — paginated commit history
- `GET /api/projects/{id}/repo/branches` — list branches with default flag
- `POST /api/projects/{id}/repo/branches` — create branch from any ref
- `DELETE /api/projects/{id}/repo/branches/{name}` — delete branch (default branch protected)
- `POST /api/projects/{id}/repo/pulls` — create pull request
- `GET /api/projects/{id}/repo/pulls?state=` — list PRs (open / closed / all)
- `GET /api/projects/{id}/repo/pulls/{number}` — get PR details
- `PUT /api/projects/{id}/repo/pulls/{number}/merge` — merge PR (merge / squash / rebase)
- `GET /api/projects/{id}/repo/collaborators` — list repo collaborators
- `POST /api/projects/{id}/repo/collaborators` — invite collaborator (pull / push / admin)
- `DELETE /api/projects/{id}/repo/collaborators/{username}` — remove collaborator
- `DATA_DICTIONARY.md` auto-synced to GitHub after every `create-table`, `alter-table`, `drop-table` — best-effort (GitHub failure never rolls back DDL)
- Octokit.NET v14 used for all GitHub API operations
- 9 new unit tests (69 total passing)

### Changed
- `GitHubOAuthService`: OAuth scope extended from `repo` to `repo,delete_repo`
- `MigrationController`: injects `IGitHubRepoService`; triggers DATA_DICTIONARY sync after DDL

---

## [0.3.0] — 2026-03-19
> Branch: `feature/admin-dashboard` → `develop`
> Commit: `a9adfe9`

### Added — Feature 3: User Record Creation & Role Management (Admin Dashboard)
- `GET /api/admin/users` — paginated user list with search, isActive, roleId filters
- `GET /api/admin/users/{id}` — single user detail (system roles + project memberships)
- `POST /api/admin/users` — onboard user from accepted GitHub collaborator
- `POST /api/admin/users/bulk` — bulk onboard multiple users
- `PUT /api/admin/users/{id}` — update user details (firstName, lastName, email, isActive)
- `PUT /api/admin/users/{id}/roles` — replace user's full role set (system + custom)
- `PUT /api/admin/users/{id}/projects/{projectId}/role` — change user's project role
- `DELETE /api/admin/users/{id}` — soft-deactivate + cascade revoke all tokens
- `GET /api/admin/roles` — list all roles (system + custom)
- `POST /api/admin/roles` — create custom role with permissions
- `PUT /api/admin/roles/{id}` — update custom role
- `DELETE /api/admin/roles/{id}` — deactivate custom role (system roles protected)
- `GET /api/admin/permissions` — list all available permissions grouped by category
- `platform.custom_roles` — admin-defined roles with permissions array
- `platform.user_custom_roles` — assigns custom roles to users
- `platform.permissions` — 8 seeded permissions across 4 categories (data, schema, project, admin)
- `RequirePermissionAttribute` — JWT-claim-based permission check; `platform_admin` bypasses all
- `AdminUserService` / `AdminRoleService` with full audit logging
- DB migration: `db/migrations/002_admin_dashboard.sql`
- 11 new unit tests (60 total passing)

### Changed
- `platform.users`: dropped `display_name`; added `first_name`, `last_name`, `onboarded_by`
- `platform.users`: `github_access_token` retained (used in Feature 4 for Claude→GitHub operations)
- App JWT: added `permissions` claim (union of all custom role permissions); replaced `display_name` with `first_name` + `last_name`
- `UpsertFromGitHubAsync`: preserves admin-set names on re-login; parses first/last from GitHub profile for new users
- Login gate: users must be org members and pre-onboarded by an admin before OAuth login succeeds

---

## [0.2.0] — 2026-03-19
> Branch: `feature/github-auth-roles` → `develop`
> PR: #1
> Commit: `4d7b624`

### Added — Feature 2: GitHub OAuth Authentication, Roles & Access Control
- GitHub OAuth2 login flow with CSRF protection (random `state` param in signed cookie)
- Short-lived app JWT (60 min) containing user profile, system roles, and all project scopes
- Refresh token (7 days) stored as SHA-256 hash, rotated on every use
- Long-lived Claude Desktop token (30 days, single-project scope, immediately revocable)
- `POST /api/auth/github` — initiates OAuth redirect
- `GET /api/auth/github/callback` — handles callback, issues token pair
- `POST /api/auth/refresh` — rotates refresh token
- `POST /api/auth/logout` — revokes refresh token
- `GET /api/auth/me` — current user profile + roles + projects
- `POST /api/auth/claude-token` — generates Claude Desktop token + ready-to-paste `mcpConfig`
- `GET /api/auth/claude-tokens` — list active Claude tokens
- `DELETE /api/auth/claude-tokens/{id}` — revoke Claude token
- `GET /api/roles` — list system roles
- `POST /api/roles/assign` / `DELETE /api/roles/revoke` — admin-only role management
- `POST /api/projects` — create project (auto-provisions Postgres schema + default roles)
- `GET /api/projects`, `GET /api/projects/{id}`, `PUT`, `DELETE` — project CRUD
- `POST /api/projects/{id}/members/invite` — invite by GitHub username
- `PUT /api/projects/{id}/members/{userId}/role` — update member role
- `DELETE /api/projects/{id}/members/{userId}` — remove member
- `GET/POST/PUT/DELETE /api/projects/{id}/roles` — custom project role management
- `platform.audit_log` — all state-changing actions are logged
- AES-256-CBC encryption for GitHub access tokens at rest
- `RequireSystemRoleAttribute` — declarative filter for `platform_admin`-only endpoints
- DB migration: `db/migrations/001_platform_schema.sql`
- 10 new unit tests (UserService + ProjectService)

---

## [0.1.0] — 2026-03-19
> Branch: `feature/supabase-proxy-api` → `develop` → `main`
> Tag: `v0.1.0`
> Commit: `48b27d6`

### Added — Feature 1: Supabase Proxy API
- Initial .NET 10 Web API — secure proxy between Claude Desktop MCP and Supabase Postgres
- Clean Architecture with 4 layers: API / Application / Domain / Infrastructure
- JWT Bearer authentication with scoped tokens (`sub`, `project_id`, `schema`, `permissions`)
- `ProjectScopeMiddleware` — extracts and validates JWT claims on every authenticated request
- Schema isolation — `SET search_path` executed before every query
- Schema name validation — must match `^project_[a-z][a-z0-9_]{2,62}$`
- Identifier validation — table/column names validated before use in DDL
- `GET /api/schema/tables` — list all tables in user's schema
- `GET /api/schema/columns` — get columns (all or per table)
- `GET /api/schema/relationships` — get foreign key relationships
- `GET /api/schema/full` — full data dictionary
- `POST /api/query/read` — SELECT only (`read` permission required); blocks DDL + DML keywords
- `POST /api/query/write` — INSERT / UPDATE / DELETE (`write` permission); blocks DDL keywords
- `POST /api/migration/create-schema` — initialize project schema (`ddl` permission)
- `POST /api/migration/create-table` — create table with column definitions + optional RLS
- `PUT /api/migration/alter-table` — add / drop / rename columns, set/drop NOT NULL
- `DELETE /api/migration/drop-table` — drop table
- `POST /api/token/generate` — issue a scoped JWT for a user + project
- `GET /health` — health check endpoint
- Rate limiting — 100 requests/min per user (fixed window)
- Scalar API docs at `/scalar/v1` (development only)
- Npgsql + Dapper for all Postgres access (SSL required)
- 47 unit tests covering all SQL validation logic (`SqlValidationHelper`)

---

## Branching Strategy

| Branch | Purpose |
|---|---|
| `main` | Production releases only |
| `develop` | Integration — all features merge here first |
| `feature/<name>` | Individual features, branched from `develop` |

## Commit Convention

| Prefix | Use |
|---|---|
| `feat:` | New feature |
| `fix:` | Bug fix |
| `refactor:` | Code improvement without behaviour change |
| `chore:` | Config, tooling, documentation |
| `test:` | Test additions or changes |
