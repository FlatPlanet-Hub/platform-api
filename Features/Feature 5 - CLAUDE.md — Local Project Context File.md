# FlatPlanet Hub — Feature 5: CLAUDE.md — Claude Code Context File

## What This Is
A `CLAUDE.md` file that users download from the Hub and place in their local project folder. It gives Claude Code everything it needs to work on the project — API endpoints, authentication token, database access rules, and coding standards.

This file is **never pushed to GitHub** — it is gitignored and stays local only.

## Purpose
Non-technical users do not write code. Claude Code does. The CLAUDE.md bridges the gap:
- User creates a project → clones the repo → downloads CLAUDE.md → drops it in the folder
- Claude Code reads CLAUDE.md and starts working immediately
- Claude Code creates tables, runs queries, writes app code — all via HubApi

---

## User Flow

```
1. User creates project (Feature 3) → GitHub repo created, Postgres schema initialized
2. User clones the repo locally
3. User opens Hub → clicks "Get CLAUDE.md" on their project
4. Hub generates CLAUDE.md with a 30-day API token scoped to this project
5. User downloads CLAUDE.md → drops it in the cloned repo folder
6. User opens Claude Code → Claude reads CLAUDE.md automatically
7. Claude Code calls HubApi endpoints to build the app
8. Token expires after 30 days → user clicks "Regenerate" → downloads new file
```

---

## API Endpoints

All require Security Platform JWT.

- `GET /api/projects/{projectId}/claude-config` — Generate CLAUDE.md

  Backend:
  1. Verify user has access to project (via Security Platform)
  2. Resolve user's permissions for this project (`read`, `write`, `ddl` based on their role)
  3. Issue a HubApi API token (JWT) containing `schema`, `projectId`, `permissions` — 30 day expiry
  4. Store token hash in `api_tokens` table
  5. Render CLAUDE.md template with project details + token + correct API paths
  6. Return content

  Response:
  ```json
  {
    "success": true,
    "data": {
      "content": "# Project Context\n\n...",
      "tokenId": "token-uuid",
      "expiresAt": "2026-04-25T00:00:00Z"
    }
  }
  ```

- `POST /api/projects/{projectId}/claude-config/regenerate` — Revoke existing token, issue new one

  Use when: token expired, token compromised, role changed.

  Backend: revokes all active API tokens for this user + project, then generates new CLAUDE.md.

- `DELETE /api/projects/{projectId}/claude-config` — Revoke token without regenerating

---

## CLAUDE.md Template

```markdown
# Project Context

## Project
- **Name**: {project.Name}
- **Description**: {project.Description}
- **Project ID**: {project.Id}
- **Schema**: {project.SchemaName}
- **Tech Stack**: {project.TechStack}

## Platform API

Base URL: {baseUrl}
Token: {apiToken}
Token Expires: {expiresAt:yyyy-MM-dd}

All API requests require this header:
Authorization: Bearer {apiToken}

## Working With the Database

### Step 0 — Check the naming dictionary BEFORE naming anything

Before creating any table, column, variable, or function, query the shared naming convention
dictionary to find the approved standard name for the concept you are working with.

```
POST {baseUrl}/api/projects/{project.Id}/query/read
{
  "sql": "SELECT field_name, data_type, format, description, example, entity FROM data_dictionary WHERE field_name ILIKE @search OR description ILIKE @search OR entity ILIKE @search ORDER BY field_name LIMIT 20",
  "parameters": { "search": "%<concept>%" }
}
```

- If the standard name exists — use it exactly as recorded in `field_name`.
- If no matching entry exists, insert a new row before proceeding:

```
POST {baseUrl}/api/projects/{project.Id}/query/write
{
  "sql": "INSERT INTO data_dictionary (field_name, data_type, format, description, example, entity, category, is_required, source) VALUES (@field_name, @data_type, @format, @description, @example, @entity, @category, @is_required, 'project') ON CONFLICT DO NOTHING",
  "parameters": {
    "field_name": "snake_case_name",
    "data_type": "text|uuid|timestamptz|boolean|numeric|...",
    "format": null,
    "description": "What this field represents",
    "example": "example_value",
    "entity": "the table or entity this belongs to",
    "category": "field",
    "is_required": false
  }
}
```

The `data_dictionary` table is shared across all projects and lives in the `public` schema.
Use `source = 'standard'` entries (populated by the platform team) as the authoritative source.
Use `source = 'project'` for new entries you add.

### Step 1 — Always read the schema first
GET {baseUrl}/api/projects/{project.Id}/schema/full

This returns all tables, columns, types, and foreign key relationships in this project.

### Create a Table
POST {baseUrl}/api/projects/{project.Id}/migration/create-table
Content-Type: application/json

{
  "tableName": "table_name",
  "columns": [
    { "name": "id", "type": "uuid", "isPrimaryKey": true, "default": "gen_random_uuid()" },
    { "name": "name", "type": "text", "nullable": false },
    { "name": "created_at", "type": "timestamptz", "default": "now()" }
  ],
  "enableRls": true
}

### Alter a Table
PUT {baseUrl}/api/projects/{project.Id}/migration/alter-table
Content-Type: application/json

{
  "tableName": "table_name",
  "operations": [
    { "action": "add", "columnName": "new_col", "type": "text" },
    { "action": "drop", "columnName": "old_col" },
    { "action": "rename", "columnName": "old_name", "newName": "new_name" }
  ]
}

### Drop a Table
DELETE {baseUrl}/api/projects/{project.Id}/migration/drop-table?table={name}

### Read Query
POST {baseUrl}/api/projects/{project.Id}/query/read
Content-Type: application/json

{
  "sql": "SELECT * FROM table_name WHERE column = @param LIMIT @limit",
  "parameters": { "param": "value", "limit": 50 }
}

### Write Query
POST {baseUrl}/api/projects/{project.Id}/query/write
Content-Type: application/json

{
  "sql": "INSERT INTO table_name (col1, col2) VALUES (@val1, @val2)",
  "parameters": { "val1": "hello", "val2": "world" }
}

## Rules
1. ALWAYS check the data dictionary (Step 0) before naming any table, column, variable, or function
2. ALWAYS read the schema (Step 1) before writing any database-related code
3. ALWAYS use @paramName syntax in queries — NEVER concatenate values into SQL strings
4. Use migration endpoints for CREATE TABLE / ALTER TABLE / DROP TABLE — never raw DDL in query endpoints
5. All database access goes through the API — NEVER connect to the database directly
6. If an API call fails, check the "success" field and "error" message in the response
7. If the token has expired, ask the user to regenerate CLAUDE.md from the Hub

## Git Workflow
1. Work on a feature branch: git checkout -b feature/{feature-name}
2. Build and test locally before committing
3. Commit with descriptive messages: feat:, fix:, refactor:, docs:
4. Push: git push origin feature/{feature-name}
5. For major features, create a PR to main

## Coding Standards
- {project.TechStack}
- Clean, readable code — add comments only where logic is non-obvious
- Handle errors gracefully — never swallow exceptions silently
- Follow naming conventions of the existing codebase

## IMPORTANT
- This file is LOCAL ONLY — do not commit or push it
- CLAUDE.md is in .gitignore — it will not appear in git status
- If the token expires, ask the user to click "Regenerate CLAUDE.md" in the Hub
```

---

## Security

1. **Token is gitignored** — `.gitignore` includes `CLAUDE.md`, seeded at repo creation (Feature 4)
2. **30 day expiry** — token stops working after 30 days
3. **Revocable** — user or admin can revoke at any time
4. **Project-scoped** — each token only accesses one project's schema
5. **Permission-scoped** — token only carries permissions the user's role has
6. **Token stored as hash** — only the SHA256 hash is stored in `api_tokens`, never the raw token
7. **One active token per user per project** — regenerating revokes the previous one automatically
