# FlatPlanet Platform API

A .NET 10 Web API that acts as a secure proxy for Supabase (PostgreSQL) and GitHub. It manages projects, project members, Claude Code API token generation, and database migrations for the FlatPlanet Hub platform.

**Authentication and identity are owned by the standalone `flatplanet-security-platform` service.** This API validates Security Platform JWTs and delegates all role and permission checks to it via HTTP.

```
Frontend            →  FlatPlanet Platform API  →  Supabase Postgres
Claude Code (MCP)   →  FlatPlanet Platform API  →  Supabase Postgres
FlatPlanet Platform API  →  Security Platform   →  IAM / RBAC
FlatPlanet Platform API  →  GitHub              →  Repo seeding / collaborators
```

---

## Architecture

Clean Architecture — 4 layers, strict one-directional dependencies:

```
FlatPlanet.Platform.API            → Controllers, Middleware, Program.cs
FlatPlanet.Platform.Application    → Services, Interfaces, DTOs
FlatPlanet.Platform.Domain         → Entities, Value Objects (no dependencies)
FlatPlanet.Platform.Infrastructure → Repositories, external service clients
FlatPlanet.Platform.Tests          → xUnit unit tests
```

Each project is isolated to its own Postgres schema (e.g. `project_acme_crm`). The API token carries the allowed schema and permissions — every query is scoped server-side before hitting the database.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| Framework | ASP.NET Core Web API |
| Database access | Npgsql + Dapper (no EF Core) |
| GitHub | Octokit.NET v14 |
| Auth | JWT Bearer — tokens issued by `flatplanet-security-platform` |
| API Docs | Scalar (development only, at `/scalar`) |
| Tests | xUnit + Moq |

---

## Getting Started

### Prerequisites

- .NET 10 SDK
- A Supabase project (use connection pooler, port 6543)
- A running instance of `flatplanet-security-platform`

### Configuration

Edit `FlatPlanet.Platform.API/appsettings.json` or supply via environment variables:

```json
{
  "Jwt": {
    "Issuer": "flatplanet-security",
    "Audience": "flatplanet-apps",
    "SecretKey": "MUST_MATCH_SECURITY_PLATFORM_SECRET_MIN_32_CHARS"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=...;Port=6543;Database=postgres;Username=...;Password=...;SSL Mode=Require"
  },
  "SecurityPlatform": {
    "BaseUrl": "https://<security-platform-host>",
    "ServiceToken": "<service-to-service-token>"
  },
  "GitHub": {
    "ServiceToken": "ghp_...",
    "OrgName": "FlatPlanet-Hub"
  },
  "Cors": {
    "AllowedOrigins": ["https://your-frontend.com"]
  }
}
```

> **Never commit real credentials.** Use environment variables, secrets management in production, or `dotnet user-secrets` for local development (a `UserSecretsId` is configured in the API project).

### Run

```bash
dotnet restore
dotnet run --project FlatPlanet.Platform.API
```

API docs: `https://localhost:{port}/scalar` (development only)

---

## API Surface

Full endpoint reference with request/response payloads: [`docs/platform-api-reference.md`](docs/platform-api-reference.md)

### Auth

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/auth/me` | Current user identity from JWT claims |

### API Tokens

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/auth/api-tokens` | Create scoped API token |
| `GET` | `/api/auth/api-tokens` | List active tokens |
| `DELETE` | `/api/auth/api-tokens/{tokenId}` | Revoke token |

### Projects

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/projects` | List projects the user has access to |
| `POST` | `/api/projects` | Create project (optional GitHub repo + CLAUDE-local.md generation). Accepts projectType (frontend/backend/database/fullstack) and authEnabled. |
| `GET` | `/api/projects/{id}` | Get project |
| `PUT` | `/api/projects/{id}` | Update project |
| `DELETE` | `/api/projects/{id}` | Deactivate project |
| `POST` | `/api/projects/{id}/sync-github-actions` | Re-sync GitHub Actions workflow and publish profile secret for projects where initial GitHub setup failed |

### Project Members

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/projects/{id}/members` | List members |
| `POST` | `/api/projects/{id}/members` | Add member |
| `PUT` | `/api/projects/{id}/members/{userId}/role` | Change member role |
| `DELETE` | `/api/projects/{id}/members/{userId}` | Remove member |

### Claude Config

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/projects/{id}/claude-config` | Generate CLAUDE-local.md content + 30-day API token |
| `POST` | `/api/projects/{id}/claude-config/regenerate` | Revoke and regenerate token |
| `DELETE` | `/api/projects/{id}/claude-config` | Revoke token |
| `GET` | `/api/projects/{id}/claude-config/workspace` | Generate CLAUDE-local.md (local only, git-ignored) + smart token management |

### File Storage

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/v1/storage/upload` | Upload a file (multipart, 50 MB max) |
| `GET` | `/api/v1/storage/files` | List files (filter by businessCode, category, tags) |
| `GET` | `/api/v1/storage/files/{fileId}/url` | Get a fresh SAS URL (60 min) |
| `DELETE` | `/api/v1/storage/files/{fileId}` | Soft-delete a file |

### DB Proxy — requires API Token

| Method | Endpoint | Permission | Description |
|---|---|---|---|
| `GET` | `/api/projects/{id}/schema/tables` | `read` | List tables |
| `GET` | `/api/projects/{id}/schema/columns` | `read` | Get columns |
| `GET` | `/api/projects/{id}/schema/relationships` | `read` | Get relationships |
| `GET` | `/api/projects/{id}/schema/full` | `read` | Full schema |
| `POST` | `/api/projects/{id}/migration/create-schema` | `ddl` | Initialize schema |
| `POST` | `/api/projects/{id}/migration/create-table` | `ddl` | Create table |
| `PUT` | `/api/projects/{id}/migration/alter-table` | `ddl` | Alter table |
| `DELETE` | `/api/projects/{id}/migration/drop-table` | `ddl` | Drop table |
| `POST` | `/api/projects/{id}/query/read` | `read` | Execute SELECT |
| `POST` | `/api/projects/{id}/query/write` | `write` | Execute INSERT / UPDATE / DELETE |

---

## File Storage

Centralized file storage is provided via Azure Blob Storage. All FlatPlanet applications share a single storage account (`flatplanetassets`), with files scoped by business code and category.

### Storage Layout

```
flatplanetassets (storage account)
  flatplanet-assets (container)
    {businessCode}/
      {category}/
        {fileId}.{ext}
```

### SAS URLs

File access is served via time-limited SAS URLs (60-minute lifetime). URLs are generated using a Managed Identity user delegation key — no storage account key is stored in the application.

### Configuration

Add the following to App Service environment variables:

```
Storage__AccountName = flatplanetassets
```

The API uses Managed Identity; no connection string or access key is required.

---

## Authentication Model

Two token types accepted, both validated against the same JWT secret:

| Token | Issued by | Used for | Lifetime |
|---|---|---|---|
| Security Platform JWT | `flatplanet-security-platform` | Frontend → HubApi | 60 min |
| HubApi API Token | HubApi `/claude-config` or `/api/auth/api-tokens` | Claude Code → DB Proxy | 30 days |

`ProjectScopeMiddleware` activates project-scope extraction only when the route contains a `{projectId}` segment and the token has `token_type = "api_token"`. All other requests pass through unconditionally.

---

## Security Controls

| Control | Implementation |
|---|---|
| Schema isolation | `SET search_path` executed before every query |
| Schema name validation | Must match `^project_[a-z0-9][a-z0-9_]{2,62}$` |
| Identifier validation | Table/column names validated against allowlist before DDL |
| Read query blocking | Blocks DML + DDL keywords: `INSERT`, `UPDATE`, `DELETE`, `DROP`, `CREATE`, `ALTER`, `TRUNCATE` |
| Write query blocking | Blocks DDL keywords only |
| Permission enforcement | Token `permissions` claim checked per endpoint |
| Parameterized queries | All user values go through Dapper parameterization — no string-concatenated SQL |
| Token storage | API tokens stored as SHA-256 hash — raw value never persisted |
| SSL | Npgsql connects to Supabase with SSL required |
| Rate limiting | 100 requests/min per user (fixed window) |

---

## Development Standards

All projects built on the FlatPlanet platform follow enforced tech stack standards. Standards are automatically injected into `CLAUDE-local.md` based on the project's `project_type`.

| Type | Stack | Deploy |
|---|---|---|
| `frontend` | React.js + TypeScript (latest) | Netlify |
| `backend` | .NET 10 / C# — Clean Architecture, SOLID, DI, design patterns | Azure App Service |
| `database` | Supabase / PostgreSQL | — |
| `fullstack` | All of the above | Netlify + Azure |

Full standards: [FLATPLANET-STANDARDS](https://github.com/FlatPlanet-Hub/FLATPLANET-STANDARDS)

---

## Running Tests

```bash
dotnet test FlatPlanet.Platform.Tests
```

76 unit tests covering SQL validation, service logic, and repository behavior.

---

## Database Migrations

Migrations live in `db/migrations/`. Run them manually against Supabase — the API does not auto-migrate.

HubApi owns two tables:

| Table | Purpose |
|---|---|
| `platform.projects` | Project registry — includes project_type, auth_enabled, app_id, GitHub fields |
| `platform.api_tokens` | Claude Code API tokens (stored as SHA-256 hash) |

Everything else (users, roles, sessions, audit) lives in the Security Platform.

---

## Branching Strategy

| Branch | Purpose |
|---|---|
| `main` | Production releases |
| `develop` | Integration — features merge here first |
| `feature/<name>` | Individual features, branched from `develop` |

Commit convention: `feat:`, `fix:`, `refactor:`, `docs:`, `chore:`, `test:`

---

## License

MIT
