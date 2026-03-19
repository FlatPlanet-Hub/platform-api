Feature 1

# Build a .NET 10 Web API — Supabase Proxy for Claude Desktop MCP

## What This Is
A secure proxy API that sits between Claude Desktop (via MCP) and Supabase. Users never get direct database credentials. They authenticate with JWT tokens scoped to their project, and the API enforces access control per schema.

## Architecture
```
Claude Desktop → MCP Server → This Proxy API (JWT auth) → Supabase Postgres
```

Each user/project gets an isolated Postgres schema (e.g. `project_abc123`). The JWT token contains the user's allowed schema, project ID, and permissions. The API validates everything server-side before touching the database.

## Tech Stack
- .NET 8 Web API (minimal API or controllers, your choice)
- Npgsql + Dapper for Postgres access
- JWT Bearer authentication
- Supabase Postgres as the database (connect via pooler on port 6543)

## Project Structure
```
SupabaseProxy/
├── Program.cs
├── appsettings.json
├── Auth/
│   ├── JwtSettings.cs
│   └── JwtService.cs
├── Middleware/
│   └── ProjectScopeMiddleware.cs
├── Models/
│   ├── Requests.cs
│   └── ApiResponse.cs
├── Services/
│   ├── IDbProxyService.cs
│   └── DbProxyService.cs
└── Controllers/
    ├── SchemaController.cs
    ├── QueryController.cs
    ├── MigrationController.cs
    └── TokenController.cs
```

## API Endpoints

### Token (called by your main app to issue tokens to users)
- `POST /api/token/generate` — Generate a scoped JWT for a user+project

### Schema / Data Dictionary (read-only)
- `GET /api/schema/tables` — List all tables in user's schema
- `GET /api/schema/columns?table={name}` — Get columns (all or per table)
- `GET /api/schema/relationships` — Get foreign key relationships
- `GET /api/schema/full` — Full data dictionary (tables + columns + relationships combined)

### Migrations / DDL (requires "ddl" permission in JWT)
- `POST /api/migration/create-table` — Create a new table
- `PUT /api/migration/alter-table` — Add/drop/rename columns
- `DELETE /api/migration/drop-table?table={name}` — Drop a table
- `POST /api/migration/create-schema` — Initialize the project schema

### Queries
- `POST /api/query/read` — Run a SELECT query (requires "read" permission)
- `POST /api/query/write` — Run INSERT/UPDATE/DELETE (requires "write" permission)

## JWT Token Structure
The token claims must include:
- `sub` — user ID
- `project_id` — project identifier
- `schema` — Postgres schema name (e.g. "project_abc123")
- `permissions` — comma-separated: "read", "write", "ddl"

## Security Requirements (CRITICAL)
1. **Schema isolation** — Every query MUST be scoped to the user's schema from the JWT. Set `search_path` before every query.
2. **Schema name validation** — Must match pattern `^[a-z][a-z0-9_]{2,62}$` and start with `project_`.
3. **Identifier validation** — All table/column names must be validated against `^[a-zA-Z_][a-zA-Z0-9_]{0,62}$` before use in SQL.
4. **Read query blocking** — Block DDL/DML keywords (DROP, DELETE, UPDATE, INSERT, ALTER, CREATE, TRUNCATE, GRANT, REVOKE) in read endpoints.
5. **Write query blocking** — Block DDL keywords (DROP, ALTER, CREATE, TRUNCATE, GRANT, REVOKE) in write endpoints.
6. **Permission enforcement** — Check JWT `permissions` claim before allowing any operation.
7. **Parameterized queries** — Use Dapper parameterization for all user-provided values.
8. **No raw connection string exposure** — Supabase credentials live only in appsettings.json server-side.
9. **Rate limiting** — Add basic rate limiting per user.

## appsettings.json Template
```json
{
  "Jwt": {
    "Issuer": "your-platform",
    "Audience": "claude-mcp",
    "SecretKey": "CHANGE_ME_MIN_32_CHARACTERS_LONG",
    "ExpiryMinutes": 60
  },
  "Supabase": {
    "Host": "aws-0-us-east-1.pooler.supabase.com",
    "Port": 6543,
    "Database": "postgres",
    "AdminUser": "postgres.YOUR_PROJECT_REF",
    "AdminPassword": "YOUR_SUPABASE_DB_PASSWORD"
  },
  "AllowedSchemaPrefix": "project_"
}
```

## Request/Response Examples

### Create Table
```json
POST /api/migration/create-table
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

### Read Query
```json
POST /api/query/read
{
  "sql": "SELECT * FROM customers WHERE created_at > @since LIMIT @limit",
  "parameters": { "since": "2025-01-01", "limit": 50 }
}
```

### Response Format (all endpoints)
```json
{
  "success": true,
  "data": [...],
  "rowsAffected": null,
  "error": null
}
```

## Additional Notes
- Use `NpgsqlConnection` with SSL required for Supabase
- Add Swagger/OpenAPI for documentation
- Add request/response logging (but mask sensitive data)
- Include a health check endpoint at `GET /health`
- Add CORS configuration for your frontend domain
- Write unit tests for the validation logic