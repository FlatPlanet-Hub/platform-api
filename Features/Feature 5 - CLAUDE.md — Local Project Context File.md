# FlatPlanet.Platform — Feature 5: CLAUDE.md — Local Project Context File

## What This Is
A `CLAUDE.md` file that users download from your app and place in their local project folder. It gives Claude Code everything it needs to work with the project — API access, endpoints, coding rules. This file is **never pushed to GitHub** — it's gitignored and stays local.

## Flow
1. User creates a project → repo is created with `CLAUDE.md` in `.gitignore`
2. User clones the repo
3. User goes to your app → clicks "Generate CLAUDE.md" → downloads the file
4. User drops it into their cloned project folder
5. Claude Code reads it automatically and starts working
6. Token expires after 30 days → user clicks "Regenerate" in your app → downloads updated file

---

## .gitignore (seeded in repo at creation — Feature 4)

The repo's `.gitignore` must include:
```
CLAUDE.md
```

This ensures the token never ends up in git history.

---

## CLAUDE.md Template

```markdown
# Project Context

## Project
- **Name**: {{PROJECT_NAME}}
- **Description**: {{PROJECT_DESCRIPTION}}
- **Project ID**: {{PROJECT_ID}}
- **Schema**: {{SCHEMA_NAME}}

## Platform API

Base URL: {{API_BASE_URL}}
Token: {{API_TOKEN}}
Token Expires: {{TOKEN_EXPIRY_DATE}}

All API requests require this header:
Authorization: Bearer {{API_TOKEN}}

### Read Schema (ALWAYS DO THIS FIRST)
Before writing any database-related code, read the current schema.

GET {{API_BASE_URL}}/api/projects/{{PROJECT_ID}}/schema/full

This returns all tables, columns, types, and relationships.

### Create Table
POST {{API_BASE_URL}}/api/projects/{{PROJECT_ID}}/migration/create-table
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

### Alter Table
PUT {{API_BASE_URL}}/api/projects/{{PROJECT_ID}}/migration/alter-table
Content-Type: application/json

{
  "tableName": "table_name",
  "actions": [
    { "action": "add", "columnName": "new_col", "type": "text" },
    { "action": "drop", "columnName": "old_col" },
    { "action": "rename", "columnName": "old_name", "newName": "new_name" },
    { "action": "alter_type", "columnName": "col", "type": "integer" }
  ]
}

### Drop Table
DELETE {{API_BASE_URL}}/api/projects/{{PROJECT_ID}}/migration/drop-table?table={name}

### Read Query
POST {{API_BASE_URL}}/api/projects/{{PROJECT_ID}}/query/read
Content-Type: application/json

{
  "sql": "SELECT * FROM table_name WHERE column = @param LIMIT @limit",
  "parameters": { "param": "value", "limit": 50 }
}

### Write Query
POST {{API_BASE_URL}}/api/projects/{{PROJECT_ID}}/query/write
Content-Type: application/json

{
  "sql": "INSERT INTO table_name (col1, col2) VALUES (@val1, @val2)",
  "parameters": { "val1": "hello", "val2": "world" }
}

## Rules
1. ALWAYS read the schema first before writing any database code
2. ALWAYS use parameterized queries — use @paramName syntax, NEVER concatenate values into SQL
3. Use migration endpoints for CREATE/ALTER/DROP — never raw DDL in query endpoints
4. Check the "success" field in every API response — handle errors gracefully
5. All database access must go through the API — NEVER connect to the database directly
6. If the token has expired, ask the user to regenerate it from the app

## Git Workflow
1. Work on a feature branch: git checkout -b feature/{feature-name}
2. Build and test locally before committing
3. Commit with descriptive messages: feat:, fix:, refactor:, docs:
4. Push: git push origin feature/{feature-name}
5. For major features, create a PR to main

## Coding Standards
- {{TECH_STACK}}
- Clean, readable code with comments where logic is complex
- Handle errors gracefully — never swallow exceptions
- Consistent naming conventions matching the existing codebase

## IMPORTANT
- This file is LOCAL ONLY — do not commit or push this file
- If the token has expired, ask the user to regenerate it from the app
```

---

## API ENDPOINTS

### Generate CLAUDE.md
- `GET /api/projects/{projectId}/claude-config` — Generate CLAUDE.md content

  Requires: user must have access to the app (checked via Feature 6 `user_app_roles`)

  Backend logic:
  1. Validate user access via Feature 6 authorization
  2. Generate API token via Feature 6: `POST /api/auth/api-tokens` (30 day expiry, scoped to app + permissions)
  3. Token stored in Feature 6 `api_tokens` table (hashed, revocable)
  4. Render the CLAUDE.md template with project details + token
  5. Return the content

  Response:
  ```json
  {
    "success": true,
    "data": {
      "content": "# Project Context\n\n## Project\n- **Name**: My SaaS App\n...",
      "tokenId": "token-uuid",
      "expiresAt": "2025-04-22T00:00:00Z"
    }
  }
  ```

### Regenerate (when token expires)
- `POST /api/projects/{projectId}/claude-config/regenerate` — Revoke old token, generate new CLAUDE.md

  Backend logic:
  1. Revoke existing API tokens for this user + app via Feature 6 `DELETE /api/auth/api-tokens/{tokenId}`
  2. Generate new API token via Feature 6
  3. Return updated CLAUDE.md content

  Response: same as above with new token

### Revoke
- `DELETE /api/projects/{projectId}/claude-config` — Revoke the API token without regenerating

### List Active Tokens
- `GET /api/auth/api-tokens` — List all active API tokens for current user (Feature 6 endpoint)
  ```json
  {
    "success": true,
    "data": [
      {
        "tokenId": "token-uuid",
        "name": "Claude token for My SaaS App",
        "appId": "app-uuid",
        "permissions": ["read", "write", "ddl"],
        "expiresAt": "2025-04-22T00:00:00Z",
        "lastUsedAt": "2025-03-23T10:00:00Z",
        "createdAt": "2025-03-23T00:00:00Z"
      }
    ]
  }
  ```

---

## DATABASE

No new tables — Feature 5 uses Feature 6's `api_tokens` table for token storage and management.

---

## SECURITY
1. **Token is gitignored** — `.gitignore` includes `CLAUDE.md`, seeded at repo creation
2. **30 day expiry** — token stops working after 30 days
3. **Revocable** — user or admin can revoke anytime
4. **Single project scope** — each token only works for one project
5. **Token stored as hash** — only the hash is in the database, not the raw token
6. **One active token per user per project** — regenerating revokes the previous one
