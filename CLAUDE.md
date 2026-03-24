# FlatPlanet Platform API — Claude Code Context

## What This Is

A .NET 10 backend API that acts as a secure proxy for Supabase (PostgreSQL), GitHub, and Claude Code integrations. It provides centralized IAM, GitHub repository operations, database migrations, and API token management for the FlatPlanet Hub platform.

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
- One repository interface per aggregate (e.g. `IUserRepository`)
- No generic repositories
- Only simple domain queries and persistence methods
- Complex read queries → separate query service

---

## Tech Stack

- .NET 10 + ASP.NET Core Web API
- PostgreSQL via Supabase (connection pooler port 6543)
- Dapper for all DB access — no EF Core
- Octokit.net for GitHub API
- JWT Bearer authentication (HS256, own issuer)
- xUnit + Moq for tests

---

## Database

- Schema prefix: `platform.` (e.g. `platform.users`, `platform.projects`)
- All queries use parameterized Dapper — never string-concatenated SQL
- Migrations live in `db/migrations/` — run them manually, the API does not auto-migrate
- Connection via `IDbConnectionFactory` — never instantiate `NpgsqlConnection` directly
- Schema name in tokens is validated via `SqlValidationHelper.IsValidSchemaName` before use

---

## Authentication Model

Two token types — both signed with the same JWT secret:

| Type | Claim `token_type` | Lifetime | Used by |
|---|---|---|---|
| App JWT | `app` | 60 min | Frontend / browser |
| API token | `api_token` | configurable days | Claude Code / CI/CD |

App JWT carries an `apps[]` JSON claim with per-app roles and permissions.
API token carries flat `schema`, `permissions`, and `app_slug` claims.

`ProjectScopeMiddleware` reads the token type and routes accordingly.

---

## IAM / Authorization

Feature 6 defines the full IAM model. Key tables:

- `platform.users` — identity
- `platform.user_oauth_links` — OAuth tokens per provider (GitHub token lives here, NOT on `users`)
- `platform.user_app_roles` — access grants (no row = no access)
- `platform.roles` + `platform.permissions` + `platform.role_permissions` — RBAC
- `platform.api_tokens` — long-lived service tokens (stored as SHA-256 hash)
- `platform.refresh_tokens` — rotation tokens (stored as SHA-256 hash)
- `platform.sessions` — active session tracking
- `platform.auth_audit_log` — immutable append-only audit log

Platform-level roles (seeded): `platform_owner`, `app_admin`
Admin access checked via `user_app_roles`, not via a JWT claim.

---

## Feature Specs

All features are documented in `Features/`. **Read the relevant spec before implementing or modifying anything in that area.**

| Feature | File | Status |
|---|---|---|
| F1 | Feature 1 - Supabase Proxy API | Done |
| F2 | Feature 2 - GitHub OAuth + JWT Token Issuance | Done |
| F3 | Feature 3 - Admin User Onboarding & Access Management | Done |
| F4 | Feature 4 - GitHub Repository Operations via Proxy API | Done |
| F5 | Feature 5 - CLAUDE.md — Local Project Context File | Done |
| F6 | Feature 6 - Flat Planet IAM — Centralized Identity & Access Management | Done |

---

## Known Issues (current branch: feature/github-repo-operations)

### Fixed (commit 1a94fcf)

1. ~~**GitHub token stored on `users` table**~~ — now reads/writes `user_oauth_links.access_token_encrypted` via `IUserOAuthLinkRepository`. `IEncryptionService` used throughout.

2. ~~**Self-registration allowed**~~ — `UpsertFromGitHubAsync` now throws `UnauthorizedAccessException` for unknown GitHub users.

3. ~~**No project permission check on repo endpoints**~~ — `GetProjectAndCheckAsync` + `CheckPermissionAsync` added to every operation with correct permission mapping (`read` / `write` / `ddl` / `manage_members`).

4. ~~**`system_roles` JWT claim never emitted**~~ — `GenerateAppToken` now accepts and emits `system_roles` as a JSON array claim. Wired end-to-end via `IUserService.GetSystemRoleNamesAsync`.

5. ~~**OAuth callback redirect broken**~~ — `FrontendCallbackUrl` from `GitHubSettings` is now used as the base URL.

6. ~~**Session record not created on login**~~ — session inserted in `IssueTokenPairAsync`; `EndAllForUserAsync` called on logout.

### Fixed (commit 52266a5)

7. ~~**`MergePullRequestAsync` uses wrong permission** (`manage_members`)~~ — changed to `"write"`. `GitHubRepoService.cs`.

8. ~~**`SyncDataDictionaryAsync` has no permission check**~~ — added `CheckPermissionAsync(userId, project, "write")` after the null/empty-repo early returns. `GitHubRepoService.cs`.

9. ~~**Logout ends all sessions instead of the current one**~~ — `IssueTokenPairAsync` creates `Session` first, links `RefreshToken.SessionId` to it. Logout calls `_sessionRepo.EndAsync(stored.SessionId.Value)` when present, falls back to `EndAllForUserAsync` for legacy tokens. `AuthController.cs`, `RefreshToken.cs`.

10. ~~**`UpsertOAuthLinkAsync` silently swallows missing GitHub provider**~~ — now throws `InvalidOperationException` when `oauth_providers` is not seeded. `UserService.cs`.

### Fixed (commit aa7cfb5)

11. ~~**`RefreshTokenRepository.CreateAsync` INSERT omits `session_id`**~~ — `session_id` added to INSERT column list and `@SessionId` to VALUES. `RefreshTokenRepository.cs`.

12. ~~**Logout fallback fires when `stored` is null**~~ — session revocation now skipped entirely when `stored` is `null`; `EndAllForUserAsync` removed. `AuthController.cs`.

### Fixed (spec compliance review)

13. ~~**`ClaudeConfigService.RenderTemplate` generates wrong API paths**~~ — paths corrected to `/api/schema/full`, `/api/migration/create-table`, `/api/query/read`. `ClaudeConfigService.cs`.

14. ~~**`RegenerateAsync` revokes tokens across all projects**~~ — token creation now sets `AppId = project.AppId`; revocation filters by `t.AppId == project.AppId`. `ClaudeConfigService.cs`.

15. ~~**`ClaudeConfigService.GetContextAsync` uses Feature 3 tables**~~ — replaced `IProjectMemberRepository`/`IProjectRoleRepository` with `IUserAppRoleRepository`/`IRolePermissionRepository`. `ClaudeConfigService.cs`.

16. ~~**OAuth route path mismatch**~~ — routes updated to `/api/auth/oauth/github` and `/api/auth/oauth/github/callback`. `AuthController.cs`.

17. ~~**`SchemaController` has no permission check**~~ — `read` permission check added to all 4 endpoints. `SchemaController.cs`.

18. ~~**No audit logging on DDL or write operations**~~ — `IAuditService` injected into `MigrationController` and `QueryController`; every DDL and write operation is now logged. `MigrationController.cs`, `QueryController.cs`.

19. ~~**Missing `PUT /api/admin/users/{id}/status` endpoint**~~ — endpoint added accepting `"active"`, `"inactive"`, `"suspended"`. `AdminUserController.cs`, `AdminUserService.cs`, `AdminUserRequests.cs`.

20. ~~**`PUT /api/admin/users/{id}/role` missing**~~ — new singular endpoint added for single app-role change via `IUserAppRoleRepository.ChangeRoleAsync`. `AdminUserController.cs`, `AdminUserService.cs`.

21. ~~**`IamAuthorizationService.AuthorizeAsync` always returns empty `Roles`**~~ — `roleRepo.GetByIdAsync` called in the role loop to populate `roleNames`. `IamAuthorizationService.cs`.

22. ~~**Resource policies never fetched in `AuthorizeAsync`**~~ — `IResourcePolicyRepository` created and injected; policies fetched by resource id and returned in response. `IamAuthorizationService.cs`, `ResourcePolicyRepository.cs`.

### Fixed (third review)

23. ~~**`UserRepository.UpdateAsync` does not include `status` column**~~ — `status = @Status` added to UPDATE. `UserRepository.cs`.

24. ~~**`QueryController` audits every read query**~~ — `query.read` audit call removed; write auditing preserved. `QueryController.cs`.

25. ~~**`ClaudeConfigService.RegenerateAsync`/`RevokeAsync` missing null `AppId` guard**~~ — both methods now throw `InvalidOperationException` if `project.AppId is null`, matching the guard in `GetContextAsync`. `ClaudeConfigService.cs`.

### Fixed (review of commit f819349)

26. ~~**`AuthorizeController` has no `[Authorize]`**~~ — `[Authorize]` added. `AuthorizeController.cs`.

27. ~~**`UpdateUserStatusAsync` missing deactivation cascade**~~ — `RevokeAllTokensAsync` now called when status is `inactive` or `suspended`, matching `DeactivateUserAsync` behaviour. `AdminUserService.cs`.

---

## Coding Conventions

- `async`/`await` for all I/O
- Depend on interfaces, not implementations — always inject via constructor
- Method length: keep under ~50 lines; extract if growing
- DTO naming: `CreateXRequest`, `UpdateXRequest`, `XResponse`, `XDto`
- Domain entity naming: pure noun (`User`, `Project`) — no `Entity` suffix unless conflict
- Tests: `MethodName_ShouldDoX_WhenCondition` naming, Arrange/Act/Assert layout
- Commit messages: `feat:`, `fix:`, `refactor:`, `docs:`, `chore:`

---

## Encryption

- AES-256 via `EncryptionHelper` (Infrastructure layer)
- Tokens/secrets hashed with SHA-256 via `EncryptionHelper.HashToken` — never store raw
- Encryption key configured via `Encryption:Key` in appsettings (min 32 bytes)
- Use `IEncryptionService` (not `EncryptionHelper` directly) from Application layer

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
