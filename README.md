# FlatPlanet Platform API

A secure .NET 10 Web API that serves as the backend platform for **FlatPlanet Hub**. Provides authenticated access to Supabase Postgres, GitHub repository management, role-based access control, and Claude Code integration — all through scoped JWT tokens with per-schema isolation.

```
Frontend / Claude Code → FlatPlanet Platform API (JWT auth) → Supabase Postgres / GitHub
```

---

## Architecture

Clean Architecture with a modular monolith layout:

```
FlatPlanet.Platform.API           → Controllers, Middleware, Program.cs
FlatPlanet.Platform.Application   → Interfaces, DTOs, Validation helpers
FlatPlanet.Platform.Domain        → Entities, Value objects
FlatPlanet.Platform.Infrastructure → DbProxyService (Npgsql + Dapper), JwtService
FlatPlanet.Platform.Tests         → xUnit unit tests
```

Each user/project is isolated to their own Postgres schema (e.g. `project_abc123`). The JWT token carries the allowed schema, project ID, and permissions. Every query is scoped server-side before hitting the database.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| Framework | ASP.NET Core Web API |
| Database access | Npgsql + Dapper |
| Auth | JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer`) |
| API Docs | Scalar (built-in .NET 10 OpenAPI) |
| Tests | xUnit |

---

## Getting Started

### Prerequisites
- .NET 10 SDK
- A Supabase project (connection pooler on port 6543)

### Configuration

Edit `FlatPlanet.Platform.API/appsettings.json`:

```json
{
  "Jwt": {
    "Issuer": "your-platform",
    "Audience": "claude-mcp",
    "SecretKey": "CHANGE_ME_MIN_32_CHARACTERS_LONG!!",
    "ExpiryMinutes": 60
  },
  "Supabase": {
    "Host": "aws-0-us-east-1.pooler.supabase.com",
    "Port": 6543,
    "Database": "postgres",
    "AdminUser": "postgres.YOUR_PROJECT_REF",
    "AdminPassword": "YOUR_SUPABASE_DB_PASSWORD"
  },
  "Cors": {
    "AllowedOrigins": ["https://your-frontend.com"]
  }
}
```

> **Never commit real credentials.** Use environment variables or secrets management in production.

### Run

```bash
dotnet restore
dotnet run --project FlatPlanet.Platform.API
```

API docs available at `https://localhost:{port}/scalar/v1` (development only).

Health check: `GET /health`

---

## API Reference

### Authentication Flow

All endpoints (except `/api/token/generate` and `/health`) require a `Bearer` JWT in the `Authorization` header.

```
Authorization: Bearer <token>
```

---

### Token

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/token/generate` | Issue a scoped JWT for a user + project |

**Request:**
```json
{
  "userId": "user-123",
  "projectId": "proj-abc",
  "schema": "project_abc",
  "permissions": "read,write,ddl"
}
```

**Response:**
```json
{
  "success": true,
  "data": "<jwt-token>",
  "rowsAffected": null,
  "error": null
}
```

**JWT Claims:**
| Claim | Description |
|---|---|
| `sub` | User ID |
| `project_id` | Project identifier |
| `schema` | Postgres schema name (must start with `project_`) |
| `permissions` | Comma-separated: `read`, `write`, `ddl` |

---

### Schema (read-only, requires JWT)

| Method | Endpoint | Permission | Description |
|---|---|---|---|
| `GET` | `/api/schema/tables` | any | List all tables in user's schema |
| `GET` | `/api/schema/columns?table={name}` | any | Get columns (all or per table) |
| `GET` | `/api/schema/relationships` | any | Get foreign key relationships |
| `GET` | `/api/schema/full` | any | Full data dictionary (tables + columns + relationships) |

---

### Queries

| Method | Endpoint | Permission | Description |
|---|---|---|---|
| `POST` | `/api/query/read` | `read` | Execute a SELECT query |
| `POST` | `/api/query/write` | `write` | Execute INSERT / UPDATE / DELETE |

**Read request:**
```json
{
  "sql": "SELECT * FROM customers WHERE created_at > @since LIMIT @limit",
  "parameters": { "since": "2025-01-01", "limit": 50 }
}
```

**Write request:**
```json
{
  "sql": "INSERT INTO customers (name, email) VALUES (@name, @email)",
  "parameters": { "name": "Jane", "email": "jane@example.com" }
}
```

---

### Migrations / DDL (requires `ddl` permission)

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/migration/create-schema` | Initialize the project schema |
| `POST` | `/api/migration/create-table` | Create a new table |
| `PUT` | `/api/migration/alter-table` | Add / drop / rename columns |
| `DELETE` | `/api/migration/drop-table?table={name}` | Drop a table |

**Create table request:**
```json
{
  "tableName": "customers",
  "columns": [
    { "name": "id", "type": "uuid", "isPrimaryKey": true, "default": "gen_random_uuid()" },
    { "name": "name", "type": "text", "nullable": false },
    { "name": "email", "type": "text", "nullable": false },
    { "name": "created_at", "type": "timestamptz", "nullable": true, "default": "now()" }
  ],
  "enableRls": true
}
```

**Alter table request:**
```json
{
  "tableName": "customers",
  "operations": [
    { "type": "AddColumn", "columnName": "phone", "dataType": "text", "nullable": true },
    { "type": "DropColumn", "columnName": "old_field" },
    { "type": "RenameColumn", "columnName": "name", "newColumnName": "full_name" }
  ]
}
```

Supported `AlterOperationType` values: `AddColumn`, `DropColumn`, `RenameColumn`, `SetNotNull`, `DropNotNull`.

---

### Standard Response Format

All endpoints return the same envelope:

```json
{
  "success": true,
  "data": [...],
  "rowsAffected": null,
  "error": null
}
```

---

## Security

| Control | Implementation |
|---|---|
| Schema isolation | `SET search_path` executed before every query |
| Schema name validation | Must match `^project_[a-z][a-z0-9_]{2,62}$` |
| Identifier validation | Table/column names validated against `^[a-zA-Z_][a-zA-Z0-9_]{0,62}$` |
| Read query blocking | Blocks `DROP, DELETE, UPDATE, INSERT, ALTER, CREATE, TRUNCATE, GRANT, REVOKE` |
| Write query blocking | Blocks DDL keywords: `DROP, ALTER, CREATE, TRUNCATE, GRANT, REVOKE` |
| Permission enforcement | JWT `permissions` claim checked per endpoint |
| Parameterized queries | All user values go through Dapper parameterization |
| Credentials | Supabase credentials are server-side only — never exposed |
| Rate limiting | 100 requests/min per user (fixed window) |
| SSL | Npgsql connects to Supabase with SSL required |

---

## Running Tests

```bash
dotnet test FlatPlanet.Platform.Tests
```

47 unit tests covering schema name validation, identifier validation, read query blocking, and write query blocking.

---

## Branching Strategy

| Branch | Purpose |
|---|---|
| `main` | Production |
| `develop` | Integration |
| `feature/<name>` | New features |

Commit convention: `feat:`, `fix:`, `refactor:`

---

## License

MIT
