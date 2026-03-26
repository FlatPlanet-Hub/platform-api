# FlatPlanet Hub — Feature 1: DB Proxy — Secure Database Access

## What This Is
A secure proxy that sits between Claude Code and Supabase PostgreSQL. Claude Code never gets direct database credentials. It authenticates with a project-scoped API token (generated in Feature 5 — CLAUDE.md) and the API enforces access control per project schema.

## Architecture
```
Claude Code (reads CLAUDE.md) → HubApi (API token auth) → Supabase Postgres
                                      ↓
                              ProjectScopeMiddleware
                              extracts schema + permissions from token
```

Each project gets an isolated Postgres schema (e.g. `project_abc123`). The API token contains the user's allowed schema, project ID, and permissions. The API validates everything server-side before touching the database.

---

## How Auth Works for DB Proxy Calls

Claude Code uses the **API token** from CLAUDE.md — NOT the user's Security Platform JWT. These are different tokens with different purposes:

| Token | Issued by | Used for | Lifetime |
|-------|-----------|----------|----------|
| Security Platform JWT | Security Platform | User auth in frontend/HubApi | 60 min |
| HubApi API Token | HubApi | Claude Code DB proxy calls | 30 days |

The `ProjectScopeMiddleware` reads the API token from the `Authorization: Bearer` header, validates it, and extracts:
- `schema` — the project's isolated Postgres schema name
- `project_id` — the project UUID
- `permissions` — what Claude Code is allowed to do (`read`, `write`, `ddl`)

---

## API Endpoints

All endpoints require a valid API token (from CLAUDE.md). Routes are scoped under the project.

### Schema
Requires `read` permission.

- `GET /api/projects/{projectId}/schema/tables` — List all tables in project schema
- `GET /api/projects/{projectId}/schema/columns?table={name}` — Get columns for a table
- `GET /api/projects/{projectId}/schema/relationships` — Get foreign key relationships
- `GET /api/projects/{projectId}/schema/full` — Full schema (tables + columns + relationships)

### Naming Convention Dictionary

The `data_dictionary` table lives in the `public` schema in Supabase and is accessible to Claude Code
via the standard read/write query endpoints. Because `DbProxyService` sets `search_path` to
`{project_schema}, public`, queries against `data_dictionary` resolve without a schema prefix.

Claude Code **must** query this table before naming any table, column, variable, or function
(see CLAUDE.md Step 0 in Feature 5).

| `source` value | Meaning |
|---|---|
| `standard` | Enterprise-approved names, populated by the platform team |
| `project` | Names added by Claude Code during a project — use these when no standard exists |

Key columns: `field_name` (the approved name), `data_type`, `format`, `description`, `example`,
`entity` (which table/entity it belongs to), `category` (default `'field'`), `is_required`.

### Queries
- `POST /api/projects/{projectId}/query/read` — Run SELECT queries (requires `read`)
  ```json
  {
    "sql": "SELECT * FROM customers WHERE created_at > @since LIMIT @limit",
    "parameters": { "since": "2025-01-01", "limit": 50 }
  }
  ```
- `POST /api/projects/{projectId}/query/write` — Run INSERT/UPDATE/DELETE (requires `write`)
  ```json
  {
    "sql": "INSERT INTO customers (name, email) VALUES (@name, @email)",
    "parameters": { "name": "John", "email": "john@example.com" }
  }
  ```

### Migrations / DDL
Requires `ddl` permission.

- `POST /api/projects/{projectId}/migration/create-schema` — Initialize project schema (called once on project creation)
- `POST /api/projects/{projectId}/migration/create-table`
  ```json
  {
    "tableName": "customers",
    "columns": [
      { "name": "id", "type": "uuid", "isPrimaryKey": true, "default": "gen_random_uuid()" },
      { "name": "name", "type": "text", "nullable": false },
      { "name": "email", "type": "text", "nullable": false },
      { "name": "created_at", "type": "timestamptz", "default": "now()" }
    ],
    "enableRls": true
  }
  ```
- `PUT /api/projects/{projectId}/migration/alter-table` — Add/drop/rename/retype columns
- `DELETE /api/projects/{projectId}/migration/drop-table?table={name}` — Drop a table

---

## Security Requirements

1. **Schema isolation** — Every query sets `search_path` to the project's schema before execution
2. **Schema name validation** — Must match `^project_[a-z][a-z0-9_]{2,62}$`
3. **Identifier validation** — All table/column names validated against `^[a-zA-Z_][a-zA-Z0-9_]{0,62}$`
4. **Read query blocking** — Block DDL/DML keywords (DROP, DELETE, UPDATE, INSERT, ALTER, CREATE, TRUNCATE, GRANT, REVOKE) in read endpoints
5. **Write query blocking** — Block DDL keywords (DROP, ALTER, CREATE, TRUNCATE, GRANT, REVOKE) in write endpoints
6. **Permission enforcement** — Check token `permissions` claim before every operation
7. **Parameterized queries** — All user-provided values go through Dapper parameterization — never concatenated
8. **Default expression whitelist** — Column defaults restricted to exact matches: `now()`, `gen_random_uuid()`, `true`, `false`, `null`, or numeric literals
9. **Audit logging** — All DDL and write operations logged

---

## Response Format
```json
{
  "success": true,
  "data": [...],
  "rowsAffected": null,
  "error": null
}
```
