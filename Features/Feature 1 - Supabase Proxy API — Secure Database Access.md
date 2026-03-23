# FlatPlanet.Platform — Feature 1: Supabase Proxy API — Secure Database Access

## What This Is
A secure .NET 8 proxy API that sits between Claude Desktop (via MCP) and Supabase. Users never get direct database credentials. They authenticate with JWT tokens scoped to their project, and the API enforces access control per schema.

## Architecture
```
Claude Desktop → MCP Server → This Proxy API (JWT auth) → Supabase Postgres
```

Each user/project gets an isolated Postgres schema (e.g. `project_abc123`). The JWT token contains the user's allowed schema, project ID, and permissions. The API validates everything server-side before touching the database.

## Tech Stack
- .NET 8 Web API
- Npgsql + Dapper for Postgres access
- JWT Bearer authentication
- Supabase Postgres as the database (connect via pooler on port 6543)

## Project Structure
```
SupabaseProxy/
├── Program.cs
├── appsettings.json
├── Middleware/
│   ├── ProjectScopeMiddleware.cs
│   └── RateLimitMiddleware.cs
├── Models/
│   ├── CreateTableRequest.cs
│   ├── AlterTableRequest.cs
│   ├── SqlQueryRequest.cs
│   └── ApiResponse.cs
├── Services/
│   ├── IDbProxyService.cs
│   └── DbProxyService.cs
└── Controllers/
    ├── SchemaController.cs
    ├── QueryController.cs
    └── MigrationController.cs
```

## API Endpoints

All endpoints require a valid JWT with project scope.

### Schema / Data Dictionary (requires "read" permission)
- `GET /api/schema/tables` — List all tables in user's schema
- `GET /api/schema/columns?table={name}` — Get columns (all or per table)
- `GET /api/schema/relationships` — Get foreign key relationships
- `GET /api/schema/full` — Full data dictionary (tables + columns + relationships combined)

### Migrations / DDL (requires "ddl" permission)
- `POST /api/migration/create-table` — Create a new table
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
- `PUT /api/migration/alter-table` — Add/drop/rename columns
- `DELETE /api/migration/drop-table?table={name}` — Drop a table
- `POST /api/migration/create-schema` — Initialize the project schema

### Queries
- `POST /api/query/read` — Run SELECT queries (requires "read")
  ```json
  {
    "sql": "SELECT * FROM customers WHERE created_at > @since LIMIT @limit",
    "parameters": { "since": "2025-01-01", "limit": 50 }
  }
  ```
- `POST /api/query/write` — Run INSERT/UPDATE/DELETE (requires "write")

## JWT Token Structure (issued by Feature 6 IAM)
The token claims this API expects:
```json
{
  "sub": "user-uuid",
  "email": "user@example.com",
  "company_id": "company-uuid",
  "apps": [
    {
      "app_id": "app-uuid",
      "app_slug": "current-project",
      "schema": "project_abc123",
      "roles": ["developer"],
      "permissions": ["read", "write", "ddl"]
    }
  ]
}
```

For API tokens (Claude/service use), the structure is simpler:
```json
{
  "sub": "user-uuid",
  "app_id": "app-uuid",
  "schema": "project_abc123",
  "permissions": ["read", "write", "ddl"],
  "token_type": "api_token"
}
```

All user records, roles, and permissions are managed by Feature 6 (Flat Planet IAM). This API only validates the JWT and enforces the permissions contained in it.

## Security Requirements
1. **Schema isolation** — Every query MUST set `search_path` to the user's schema from JWT before execution.
2. **Schema name validation** — Must match `^[a-z][a-z0-9_]{2,62}$` and start with `project_`.
3. **Identifier validation** — All table/column names validated against `^[a-zA-Z_][a-zA-Z0-9_]{0,62}$`.
4. **Read query blocking** — Block DDL/DML keywords (DROP, DELETE, UPDATE, INSERT, ALTER, CREATE, TRUNCATE, GRANT, REVOKE) in read endpoints.
5. **Write query blocking** — Block DDL keywords (DROP, ALTER, CREATE, TRUNCATE, GRANT, REVOKE) in write endpoints.
6. **Permission enforcement** — Check JWT `permissions` claim before allowing any operation.
7. **Parameterized queries** — Use Dapper parameterization for all user-provided values.
8. **No connection string exposure** — Supabase credentials only in appsettings.json.
9. **Rate limiting** — Per user, per endpoint.
10. **Audit logging** — Log every DDL and write operation to `platform.audit_log`.

## Response Format (all endpoints)
```json
{
  "success": true,
  "data": [...],
  "rowsAffected": null,
  "error": null
}
```

## appsettings.json
```json
{
  "Jwt": {
    "Issuer": "your-platform",
    "Audience": "claude-mcp",
    "SecretKey": "CHANGE_ME_MIN_32_CHARACTERS_LONG!!"
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

## Additional Requirements
- Add Swagger/OpenAPI documentation
- Health check at `GET /health`
- CORS configuration
- Request/response logging (mask sensitive data)
- Global exception handler middleware
- Unit tests for validation logic
- Dependency injection for all services
