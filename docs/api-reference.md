# FlatPlanet Platform API — Frontend Integration Reference

**Base URL:** `https://localhost:7xxx` (see `launchSettings.json` for exact port)
**API Docs (dev only):** `/scalar`

---

## Table of Contents

1. [Authentication Overview](#authentication-overview)
2. [GitHub OAuth Flow](#github-oauth-flow)
3. [Token Refresh](#token-refresh)
4. [Logout](#logout)
5. [Current User Profile](#current-user-profile)
6. [API Tokens](#api-tokens)
7. [Authorization Check](#authorization-check)
8. [Schema Inspection](#schema-inspection)
9. [Query — Read](#query--read)
10. [Query — Write](#query--write)
11. [Migrations](#migrations)
12. [GitHub Repo Operations](#github-repo-operations)
13. [Projects](#projects)
14. [Project Members](#project-members)
15. [Apps](#apps)
16. [Companies](#companies)
17. [Resources](#resources)
18. [Admin — Users](#admin--users)
19. [Admin — Roles & Permissions](#admin--roles--permissions)
20. [Audit Log](#audit-log)
21. [Compliance](#compliance)
22. [Claude Config](#claude-config)
23. [Error Reference](#error-reference)

---

## Authentication Overview

All protected endpoints require:

```
Authorization: Bearer <access_token>
```

Two token types exist. The `token_type` JWT claim determines routing behavior:

| Token Type | `token_type` claim | Lifetime | Used by |
|---|---|---|---|
| App JWT | `app` | 60 min | Frontend / browser |
| API Token | `api_token` | Configurable (default 30 days) | Claude Code / CI/CD |

**App JWT** — obtained from the GitHub OAuth flow. Carries an `apps[]` claim with per-app roles and permissions.

**API Token** — created via `POST /api/auth/api-tokens`. Carries flat `schema`, `permissions`, and `app_slug` claims. Required for Schema, Query, and Migration endpoints.

> **Note:** The middleware returns `403` before reaching any controller if an API token has an invalid or missing schema claim.

---

## GitHub OAuth Flow

### Step 1 — Initiate OAuth

### `GET /api/auth/oauth/github`

Redirects the user to GitHub's OAuth authorization page.

No request body. No auth required.

**Behavior:**
- Sets an `oauth_state` cookie (HttpOnly, used for CSRF validation on callback)
- Redirects the browser to GitHub

**Trigger this by navigating the browser window directly** — do not call via `fetch`.

---

### Step 2 — OAuth Callback

### `GET /api/auth/oauth/github/callback`

GitHub redirects here after the user authorizes. Handled server-side.

| Query Param | Type | Description |
|---|---|---|
| `code` | string | Authorization code from GitHub |
| `state` | string | Must match the `oauth_state` cookie value |

**Behavior on success:**
- Validates `state` cookie
- Exchanges `code` for a GitHub access token
- Looks up the user by GitHub ID — **self-registration is not allowed**; user must already exist in `platform.users`
- Creates a session and issues a token pair
- Redirects to `{FrontendCallbackUrl}?token=<access_token>&refresh=<refresh_token>`

**Errors:**

| Condition | Result |
|---|---|
| `state` mismatch | `400` redirect with error |
| GitHub user not in platform | `403` redirect with error |
| GitHub API failure | `500` redirect with error |

> **Note:** Store the `access_token` and `refresh_token` from the redirect URL query params. The access token expires in 60 minutes — implement silent refresh using the refresh token.

---

## Token Refresh

### `POST /api/auth/refresh`

Rotates the refresh token and issues a new access/refresh token pair.

No auth header required.

### Request

```json
{
  "refreshToken": "eyJhbGc..."
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `refreshToken` | string | Yes | The current valid refresh token |

### Success Response — `200`

```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresAt": "2026-03-23T15:30:00Z"
}
```

### Error Responses

| Status | Condition |
|---|---|
| `401` | Refresh token not found or already revoked |
| `409` | Refresh token reuse detected (token already consumed) |
| `500` | DB failure during rotation |

### Notes

- The old refresh token is **immediately revoked** on use — do not call this twice in parallel for the same token
- On `409`, the session may be compromised; redirect the user to login
- `expiresAt` is the access token expiry, not the refresh token expiry

---

## Logout

### `POST /api/auth/logout`

Revokes the refresh token and ends the current session.

**Requires:** `Authorization: Bearer <access_token>`

### Request

```json
{
  "refreshToken": "eyJhbGc..."
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `refreshToken` | string | Yes | The active refresh token for this session |

### Success Response — `200`

```json
{
  "success": true,
  "data": null,
  "error": null
}
```

### Error Responses

| Status | Condition |
|---|---|
| `401` | Missing or expired access token |
| `404` | Refresh token not found |

### Notes

- Only the session associated with the provided refresh token is ended — other active sessions for the same user are unaffected
- After logout, discard both tokens from storage

---

## Current User Profile

### `GET /api/auth/me`

Returns the authenticated user's profile, system roles, and project memberships.

**Requires:** `Authorization: Bearer <access_token>`

No request body.

### Success Response — `200`

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "gitHubUsername": "john-doe",
  "firstName": "John",
  "lastName": "Doe",
  "email": "john@example.com",
  "avatarUrl": "https://avatars.githubusercontent.com/u/12345678",
  "systemRoles": ["platform_owner"],
  "projects": [
    {
      "projectId": "a1b2c3d4-0000-0000-0000-000000000001",
      "name": "Billing Service",
      "schema": "project_billing",
      "projectRole": "admin",
      "permissions": ["read", "write", "ddl"]
    }
  ]
}
```

### Fields — Response

| Field | Type | Description |
|---|---|---|
| `id` | Guid | Platform user ID |
| `gitHubUsername` | string | GitHub login handle |
| `firstName` | string? | Optional display name |
| `lastName` | string? | Optional display name |
| `email` | string? | GitHub email (may be null if private) |
| `avatarUrl` | string? | GitHub avatar URL |
| `systemRoles` | string[] | Platform-level roles (`platform_owner`, `app_admin`) |
| `projects[].projectId` | Guid | Project ID |
| `projects[].schema` | string | Schema name used in API token claims |
| `projects[].permissions` | string[] | `read`, `write`, `ddl`, `manage_members` |

### Error Responses

| Status | Condition |
|---|---|
| `401` | Missing or expired access token |
| `404` | User record deleted after token was issued |

---

## API Tokens

Long-lived tokens for use with Claude Code or CI/CD. Carry schema + permission claims.

### Create API Token

### `POST /api/auth/api-tokens`

**Requires:** `Authorization: Bearer <access_token>`

### Request

```json
{
  "name": "billing-ci-token",
  "appId": "a1b2c3d4-0000-0000-0000-000000000099",
  "permissions": ["read", "write"],
  "expiryDays": 30
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Human-readable label |
| `appId` | Guid? | No | Scopes token to a specific app |
| `permissions` | string[] | Yes | `read`, `write`, `ddl`, `manage_members` |
| `expiryDays` | int | No | Days until expiry. Default: `30` |

### Success Response — `200`

```json
{
  "tokenId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "name": "billing-ci-token",
  "permissions": ["read", "write"],
  "expiresAt": "2026-04-22T10:00:00Z",
  "mcpConfig": {
    "mcpServers": {
      "flatplanet": {
        "command": "npx",
        "args": ["-y", "@flatplanet/mcp-server"],
        "env": {
          "FLATPLANET_TOKEN": "eyJhbGc..."
        }
      }
    }
  }
}
```

> **Note:** `token` is returned **once only** — it is stored as a SHA-256 hash server-side. Save it immediately.

### Error Responses

| Status | Condition |
|---|---|
| `401` | Not authenticated |
| `400` | Invalid permissions value |

---

### List API Tokens

### `GET /api/auth/api-tokens`

**Requires:** `Authorization: Bearer <access_token>`

### Success Response — `200`

```json
[
  {
    "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "name": "billing-ci-token",
    "appId": "a1b2c3d4-0000-0000-0000-000000000099",
    "permissions": ["read", "write"],
    "expiresAt": "2026-04-22T10:00:00Z",
    "lastUsedAt": "2026-03-23T08:00:00Z",
    "createdAt": "2026-03-23T10:00:00Z"
  }
]
```

The raw token value is **never returned** in list responses.

---

### Revoke API Token

### `DELETE /api/auth/api-tokens/{tokenId}`

**Requires:** `Authorization: Bearer <access_token>`

| Path Param | Type | Description |
|---|---|---|
| `tokenId` | Guid | Token to revoke |

### Success Response — `200`

```json
{
  "success": true,
  "data": null,
  "error": null
}
```

### Error Responses

| Status | Condition |
|---|---|
| `404` | Token not found or already revoked |
| `403` | Token belongs to a different user |

---

## Authorization Check

### `POST /api/authorize`

Checks whether a user is allowed to perform an action on a resource. Use this for server-side access decisions in other services.

**Requires:** `Authorization: Bearer <access_token>`

### Request

```json
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "appSlug": "billing-app",
  "resourceIdentifier": "invoices/inv_00291",
  "requiredPermission": "write"
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `userId` | Guid | Yes | User to check |
| `appSlug` | string | Yes | App slug the resource belongs to |
| `resourceIdentifier` | string | Yes | Resource identifier string |
| `requiredPermission` | string? | No | Specific permission to check (`read`, `write`, etc.) |

### Success Response — `200`

```json
{
  "allowed": true,
  "roles": ["app_admin"],
  "permissions": ["read", "write"],
  "policies": {
    "data_region": "eu-west-1"
  },
  "mfaRequired": false
}
```

### Error Responses

| Status | Condition |
|---|---|
| `401` | Not authenticated |
| `404` | User or app not found |

---

## Schema Inspection

All schema endpoints require an **API token** with `read` permission. The schema is derived from the token's `schema` claim.

**Requires:** `Authorization: Bearer <api_token>`

---

### Get All Tables

### `GET /api/schema/tables`

### Success Response — `200`

```json
[
  {
    "tableName": "invoices",
    "tableType": "BASE TABLE"
  },
  {
    "tableName": "invoice_items",
    "tableType": "BASE TABLE"
  }
]
```

---

### Get Columns

### `GET /api/schema/columns`

| Query Param | Type | Required | Description |
|---|---|---|---|
| `table` | string | No | Filter by table name. Omit for all columns. |

### Success Response — `200`

```json
[
  {
    "tableName": "invoices",
    "columnName": "id",
    "dataType": "uuid",
    "isNullable": false,
    "columnDefault": "gen_random_uuid()",
    "ordinalPosition": 1
  },
  {
    "tableName": "invoices",
    "columnName": "amount",
    "dataType": "numeric",
    "isNullable": false,
    "columnDefault": null,
    "ordinalPosition": 3
  }
]
```

---

### Get Relationships

### `GET /api/schema/relationships`

### Success Response — `200`

```json
[
  {
    "constraintName": "invoice_items_invoice_id_fkey",
    "tableName": "invoice_items",
    "columnName": "invoice_id",
    "foreignTableName": "invoices",
    "foreignColumnName": "id"
  }
]
```

---

### Get Full Schema

### `GET /api/schema/full`

Returns tables, columns, and relationships in a single call.

### Success Response — `200`

```json
{
  "tables": [...],
  "columns": [...],
  "relationships": [...]
}
```

### Error Responses (all schema endpoints)

| Status | Condition |
|---|---|
| `401` | Missing or invalid token |
| `403` | Token type is `app` (must use API token), or schema claim missing/invalid |

---

## Query — Read

### `POST /api/query/read`

Executes a parameterized SELECT against the project's schema.

**Requires:** `Authorization: Bearer <api_token>` with `read` permission.

### Request

```json
{
  "sql": "SELECT id, amount, status FROM invoices WHERE status = @status LIMIT 50",
  "parameters": {
    "status": "pending"
  }
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `sql` | string | Yes | SELECT query. Must be validated SQL — no DDL or DML |
| `parameters` | object? | No | Named parameters. Use `@paramName` in SQL |

### Success Response — `200`

```json
{
  "success": true,
  "data": [
    {
      "id": "inv_00291",
      "amount": 1500.00,
      "status": "pending"
    }
  ],
  "error": null
}
```

### Error Responses

| Status | Condition |
|---|---|
| `400` | SQL fails validation (non-SELECT detected, invalid syntax) |
| `403` | Token lacks `read` permission |
| `500` | DB error (bad table name, type mismatch, etc.) |

### Notes

- Only SELECT queries are accepted — INSERT/UPDATE/DELETE/DDL will be rejected at validation
- The schema is injected from the API token claim — do not prefix table names with the schema
- Read queries are **not** audit-logged

---

## Query — Write

### `POST /api/query/write`

Executes a parameterized INSERT, UPDATE, or DELETE.

**Requires:** `Authorization: Bearer <api_token>` with `write` permission.

### Request

```json
{
  "sql": "UPDATE invoices SET status = @status WHERE id = @id",
  "parameters": {
    "status": "paid",
    "id": "inv_00291"
  }
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `sql` | string | Yes | DML query (INSERT / UPDATE / DELETE) |
| `parameters` | object? | No | Named parameters |

### Success Response — `200`

```json
{
  "success": true,
  "data": null,
  "rowsAffected": 1,
  "error": null
}
```

### Error Responses

| Status | Condition |
|---|---|
| `400` | SQL fails validation |
| `403` | Token lacks `write` permission |
| `409` | Unique constraint violation |
| `500` | DB error |

### Notes

- Every write query is audit-logged with event `query.write`
- `rowsAffected` will be `0` if the WHERE clause matched nothing — this is a `200`, not an error

---

## Migrations

All migration endpoints require an **API token** with `ddl` permission. Every operation is audit-logged.

**Requires:** `Authorization: Bearer <api_token>` with `ddl` permission.

---

### Create Schema

### `POST /api/migration/create-schema`

Creates the schema defined in the API token's `schema` claim if it does not exist.

No request body.

### Success Response — `200`

```json
{
  "success": true,
  "data": null,
  "error": null
}
```

---

### Create Table

### `POST /api/migration/create-table`

### Request

```json
{
  "tableName": "invoices",
  "columns": [
    {
      "name": "id",
      "dataType": "uuid",
      "nullable": false,
      "defaultValue": "gen_random_uuid()",
      "isPrimaryKey": true
    },
    {
      "name": "amount",
      "dataType": "numeric(10,2)",
      "nullable": false
    },
    {
      "name": "created_at",
      "dataType": "timestamptz",
      "nullable": false,
      "defaultValue": "now()"
    }
  ],
  "enableRls": true
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `tableName` | string | Yes | Table name (no schema prefix) |
| `columns` | array | Yes | Column definitions |
| `columns[].name` | string | Yes | Column name |
| `columns[].dataType` | string | Yes | PostgreSQL data type |
| `columns[].nullable` | bool | Yes | Whether column accepts NULL |
| `columns[].defaultValue` | string? | No | SQL default expression |
| `columns[].isPrimaryKey` | bool? | No | Marks column as PK |
| `enableRls` | bool | Yes | Enables Row Level Security on the table |

### Success Response — `200`

```json
{
  "success": true,
  "data": null,
  "error": null
}
```

### Error Responses

| Status | Condition |
|---|---|
| `400` | Invalid table name or column definition |
| `409` | Table already exists |
| `403` | Token lacks `ddl` permission |

---

### Alter Table

### `PUT /api/migration/alter-table`

### Request

```json
{
  "tableName": "invoices",
  "operations": [
    {
      "type": "AddColumn",
      "columnName": "paid_at",
      "dataType": "timestamptz",
      "nullable": true
    },
    {
      "type": "RenameColumn",
      "columnName": "amount",
      "newColumnName": "total_amount"
    }
  ]
}
```

### Fields — `operations[]`

| Field | Type | Required | Description |
|---|---|---|---|
| `type` | string | Yes | `AddColumn`, `DropColumn`, `RenameColumn`, `SetNotNull`, `DropNotNull` |
| `columnName` | string | Yes | Target column |
| `newColumnName` | string? | For `RenameColumn` | New name |
| `dataType` | string? | For `AddColumn` | PostgreSQL data type |
| `nullable` | bool? | For `AddColumn` | Whether column is nullable |

### Success Response — `200`

```json
{
  "success": true,
  "data": null,
  "error": null
}
```

### Error Responses

| Status | Condition |
|---|---|
| `404` | Table not found in schema |
| `409` | Column already exists (on AddColumn) |
| `400` | DropNotNull on a column with no NOT NULL constraint |

---

### Drop Table

### `DELETE /api/migration/drop-table`

| Query Param | Type | Required | Description |
|---|---|---|---|
| `table` | string | Yes | Table name to drop |

### Success Response — `200`

```json
{
  "success": true,
  "data": null,
  "error": null
}
```

### Error Responses

| Status | Condition |
|---|---|
| `404` | Table not found |
| `409` | Other tables have FK references to this table |

---

## GitHub Repo Operations

All repo endpoints require an **App JWT** and are scoped to a project.

**Requires:** `Authorization: Bearer <access_token>`

**Base path:** `/api/projects/{projectId}/repo`

The authenticated user must have the appropriate project permission for each operation (see per-endpoint notes).

---

### Create Repository

### `POST /api/projects/{projectId}/repo`

Requires `manage_members` permission on project.

### Request

```json
{
  "repoName": "billing-service",
  "description": "Billing microservice for FlatPlanet",
  "isPrivate": true,
  "org": "flatplanet-io"
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `repoName` | string | Yes | Repository name (no spaces) |
| `description` | string? | No | Repo description |
| `isPrivate` | bool | No | Default: `true` |
| `org` | string? | No | GitHub org name. Null = personal account |

### Success Response — `200`

```json
{
  "repoFullName": "flatplanet-io/billing-service",
  "repoUrl": "https://github.com/flatplanet-io/billing-service",
  "cloneUrl": "https://github.com/flatplanet-io/billing-service.git",
  "defaultBranch": "main"
}
```

### Error Responses

| Status | Condition |
|---|---|
| `403` | User lacks GitHub OAuth token or project permission |
| `409` | Repository already exists |
| `500` | GitHub API error |

---

### Get Repository

### `GET /api/projects/{projectId}/repo`

Requires `read` permission.

### Success Response — `200`

```json
{
  "repoFullName": "flatplanet-io/billing-service",
  "repoUrl": "https://github.com/flatplanet-io/billing-service",
  "cloneUrl": "https://github.com/flatplanet-io/billing-service.git",
  "defaultBranch": "main"
}
```

---

### Delete Repository

### `DELETE /api/projects/{projectId}/repo`

Requires `manage_members` permission. This is **irreversible**.

**Required Header:**

```
X-Confirm-Delete: true
```

### Success Response — `200`

```json
{
  "success": true,
  "data": null,
  "error": null
}
```

### Error Responses

| Status | Condition |
|---|---|
| `400` | `X-Confirm-Delete` header missing or not `true` |
| `403` | Lacks permission |
| `404` | Repo not found |

---

### Get File or Directory

### `GET /api/projects/{projectId}/repo/files`

Requires `read` permission.

| Query Param | Type | Required | Description |
|---|---|---|---|
| `path` | string | No | File or directory path. Omit for root. |
| `ref_` | string | No | Branch, tag, or commit SHA. Default: repo default branch. |

### Success Response — `200` (file)

```json
{
  "type": "file",
  "name": "README.md",
  "path": "README.md",
  "content": "# Billing Service\n...",
  "sha": "abc123def456",
  "size": 1024
}
```

### Success Response — `200` (directory)

```json
{
  "type": "dir",
  "path": "src",
  "items": [
    {
      "name": "index.ts",
      "path": "src/index.ts",
      "type": "file",
      "size": 512
    }
  ]
}
```

---

### Get Repository Tree

### `GET /api/projects/{projectId}/repo/tree`

Requires `read` permission. Returns the full file tree.

| Query Param | Type | Required | Description |
|---|---|---|---|
| `ref_` | string | No | Branch/tag/SHA. Default: default branch. |

### Success Response — `200`

```json
{
  "sha": "abc123",
  "tree": [
    {
      "path": "src/index.ts",
      "type": "blob",
      "size": 512
    },
    {
      "path": "src",
      "type": "tree",
      "size": null
    }
  ]
}
```

---

### Create or Update File

### `PUT /api/projects/{projectId}/repo/files`

Requires `write` permission.

### Request

```json
{
  "path": "src/utils/helpers.ts",
  "content": "export const add = (a: number, b: number) => a + b;\n",
  "message": "feat: add helper utility",
  "branch": "main",
  "sha": null
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `path` | string | Yes | File path in repo |
| `content` | string | Yes | Full file content (UTF-8) |
| `message` | string | Yes | Commit message |
| `branch` | string | Yes | Target branch |
| `sha` | string? | For updates | Current file SHA — required when updating an existing file |

### Success Response — `200`

```json
{
  "type": "file",
  "name": "helpers.ts",
  "path": "src/utils/helpers.ts",
  "content": "export const add = ...",
  "sha": "def456abc789",
  "size": 52
}
```

### Error Responses

| Status | Condition |
|---|---|
| `409` | Updating file but `sha` is missing or stale |
| `404` | Branch not found |

### Notes

- Creating a new file: omit `sha`
- Updating an existing file: get current `sha` from `GET /files`, include it here
- Stale `sha` (file was updated by someone else) → `409`

---

### Delete File

### `DELETE /api/projects/{projectId}/repo/files`

Requires `write` permission.

### Request

```json
{
  "path": "src/utils/helpers.ts",
  "message": "chore: remove helpers file",
  "branch": "main",
  "sha": "def456abc789"
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `path` | string | Yes | File path |
| `message` | string | Yes | Commit message |
| `branch` | string | Yes | Branch |
| `sha` | string | Yes | Current file SHA (from `GET /files`) |

### Error Responses

| Status | Condition |
|---|---|
| `404` | File not found |
| `409` | SHA mismatch |

---

### Create Commit (Multiple Files)

### `POST /api/projects/{projectId}/repo/commits`

Requires `write` permission. Creates a single commit with multiple file changes.

### Request

```json
{
  "message": "feat: add invoice module",
  "branch": "feature/invoices",
  "files": [
    {
      "path": "src/invoice.ts",
      "action": "create",
      "content": "export class Invoice {}"
    },
    {
      "path": "src/legacy.ts",
      "action": "delete",
      "content": null
    }
  ]
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `message` | string | Yes | Commit message |
| `branch` | string | Yes | Target branch |
| `files` | array | Yes | Files to include in the commit |
| `files[].path` | string | Yes | File path in repo |
| `files[].action` | string | Yes | `create`, `update`, `delete` |
| `files[].content` | string? | For `create`/`update` | File content. Omit for `delete`. |

### Success Response — `200`

```json
{
  "commitSha": "abc123def456789",
  "commitUrl": "https://github.com/flatplanet-io/billing-service/commit/abc123",
  "filesChanged": 2
}
```

---

### List Commits

### `GET /api/projects/{projectId}/repo/commits`

Requires `read` permission.

| Query Param | Type | Required | Description |
|---|---|---|---|
| `branch` | string | No | Filter by branch. Default: default branch. |
| `page` | int | No | Default: `1` |
| `pageSize` | int | No | Default: `20` |

### Success Response — `200`

```json
[
  {
    "sha": "abc123",
    "message": "feat: add invoice module",
    "author": "john-doe",
    "date": "2026-03-23T10:00:00+00:00"
  }
]
```

---

### List Branches

### `GET /api/projects/{projectId}/repo/branches`

Requires `read` permission.

### Success Response — `200`

```json
[
  {
    "name": "main",
    "isDefault": true,
    "sha": "abc123"
  },
  {
    "name": "feature/invoices",
    "isDefault": false,
    "sha": "def456"
  }
]
```

---

### Create Branch

### `POST /api/projects/{projectId}/repo/branches`

Requires `write` permission.

### Request

```json
{
  "name": "feature/payments",
  "fromBranch": "main"
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | New branch name |
| `fromBranch` | string | Yes | Source branch to branch from |

### Success Response — `200`

```json
{
  "name": "feature/payments",
  "isDefault": false,
  "sha": "abc123"
}
```

### Error Responses

| Status | Condition |
|---|---|
| `409` | Branch already exists |
| `404` | Source branch not found |

---

### Delete Branch

### `DELETE /api/projects/{projectId}/repo/branches/{branchName}`

Requires `write` permission.

| Path Param | Type | Description |
|---|---|---|
| `branchName` | string | Branch to delete |

### Error Responses

| Status | Condition |
|---|---|
| `403` | Attempting to delete the default branch |
| `404` | Branch not found |

---

### Create Pull Request

### `POST /api/projects/{projectId}/repo/pulls`

Requires `write` permission.

### Request

```json
{
  "title": "feat: add payment module",
  "body": "Adds invoice creation and payment tracking.",
  "head": "feature/payments",
  "base": "main"
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `title` | string | Yes | PR title |
| `body` | string? | No | PR description (Markdown supported) |
| `head` | string | Yes | Source branch |
| `base` | string | Yes | Target branch |

### Success Response — `200`

```json
{
  "number": 42,
  "title": "feat: add payment module",
  "state": "open",
  "head": "feature/payments",
  "base": "main",
  "url": "https://github.com/flatplanet-io/billing-service/pull/42",
  "author": "john-doe",
  "createdAt": "2026-03-23T10:00:00+00:00"
}
```

### Error Responses

| Status | Condition |
|---|---|
| `409` | PR already exists for this head/base combination |
| `422` | No commits between head and base |

---

### List Pull Requests

### `GET /api/projects/{projectId}/repo/pulls`

Requires `read` permission.

| Query Param | Type | Required | Description |
|---|---|---|---|
| `state` | string | No | `open`, `closed`, `all`. Default: `open` |

### Success Response — `200`

```json
[
  {
    "number": 42,
    "title": "feat: add payment module",
    "state": "open",
    "head": "feature/payments",
    "base": "main",
    "url": "https://github.com/flatplanet-io/billing-service/pull/42",
    "author": "john-doe",
    "createdAt": "2026-03-23T10:00:00+00:00"
  }
]
```

---

### Get Pull Request

### `GET /api/projects/{projectId}/repo/pulls/{prNumber}`

Requires `read` permission.

| Path Param | Type | Description |
|---|---|---|
| `prNumber` | int | PR number |

---

### Merge Pull Request

### `PUT /api/projects/{projectId}/repo/pulls/{prNumber}/merge`

Requires `write` permission.

### Request

```json
{
  "mergeMethod": "squash"
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `mergeMethod` | string | No | `merge`, `squash`, `rebase`. Default: `merge` |

### Success Response — `200`

```json
{
  "commitSha": "abc123def456",
  "merged": true,
  "message": "Pull request successfully merged"
}
```

### Error Responses

| Status | Condition |
|---|---|
| `409` | Merge conflict — resolve manually |
| `422` | PR is not mergeable (checks failed, draft PR, etc.) |
| `404` | PR not found |

---

### List Collaborators

### `GET /api/projects/{projectId}/repo/collaborators`

Requires `read` permission.

### Success Response — `200`

```json
[
  {
    "gitHubUsername": "jane-smith",
    "avatarUrl": "https://avatars.githubusercontent.com/u/87654321",
    "permission": "push"
  }
]
```

---

### Invite Collaborator

### `POST /api/projects/{projectId}/repo/collaborators`

Requires `manage_members` permission.

### Request

```json
{
  "gitHubUsername": "jane-smith",
  "permission": "push"
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `gitHubUsername` | string | Yes | GitHub handle to invite |
| `permission` | string | Yes | `pull` (read), `push` (write), `admin` |

### Error Responses

| Status | Condition |
|---|---|
| `404` | GitHub user not found |
| `409` | User is already a collaborator |

---

### Remove Collaborator

### `DELETE /api/projects/{projectId}/repo/collaborators/{githubUsername}`

Requires `manage_members` permission.

---

## Projects

**Requires:** `Authorization: Bearer <access_token>`

### Create Project

### `POST /api/projects`

### Request

```json
{
  "name": "Billing Service",
  "description": "Handles invoicing and payment processing"
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Project display name |
| `description` | string? | No | Optional description |

### Success Response — `200`

```json
{
  "id": "a1b2c3d4-0000-0000-0000-000000000001",
  "name": "Billing Service",
  "description": "Handles invoicing and payment processing",
  "schemaName": "project_billing_service",
  "ownerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "gitHubRepo": null,
  "isActive": true,
  "createdAt": "2026-03-23T10:00:00Z",
  "members": []
}
```

---

### List Projects

### `GET /api/projects`

Returns projects the authenticated user has access to.

### Success Response — `200`

```json
[
  {
    "id": "a1b2c3d4-0000-0000-0000-000000000001",
    "name": "Billing Service",
    "schemaName": "project_billing_service",
    "ownerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "isActive": true,
    "createdAt": "2026-03-23T10:00:00Z"
  }
]
```

---

### Get Project

### `GET /api/projects/{id}`

### Success Response — `200`

Full `ProjectResponse` object including `members[]`.

---

### Update Project

### `PUT /api/projects/{id}`

### Request

```json
{
  "name": "Billing V2",
  "description": "Updated billing system",
  "gitHubRepo": "flatplanet-io/billing-service"
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string? | No | New project name |
| `description` | string? | No | New description |
| `gitHubRepo` | string? | No | GitHub repo full name (org/repo) |

---

### Delete Project

### `DELETE /api/projects/{id}`

### Error Responses

| Status | Condition |
|---|---|
| `403` | Not project owner or admin |
| `409` | Project has active members |

---

## Project Members

**Base path:** `/api/projects/{projectId}/members`
**Requires:** `Authorization: Bearer <access_token>`

### List Members

### `GET /api/projects/{projectId}/members`

### Success Response — `200`

```json
[
  {
    "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "gitHubUsername": "john-doe",
    "firstName": "John",
    "lastName": "Doe",
    "avatarUrl": "https://avatars.githubusercontent.com/u/12345678",
    "roleName": "admin",
    "permissions": ["read", "write", "ddl"],
    "joinedAt": "2026-03-01T09:00:00Z"
  }
]
```

---

### Invite Member

### `POST /api/projects/{projectId}/members/invite`

### Request

```json
{
  "gitHubUsername": "jane-smith",
  "role": "developer"
}
```

### Error Responses

| Status | Condition |
|---|---|
| `404` | GitHub username not found in platform |
| `409` | User is already a project member |

---

### Update Member Role

### `PUT /api/projects/{projectId}/members/{targetUserId}/role`

### Request

```json
{
  "role": "viewer"
}
```

---

### Remove Member

### `DELETE /api/projects/{projectId}/members/{targetUserId}`

### Error Responses

| Status | Condition |
|---|---|
| `403` | Cannot remove the project owner |

---

## Apps

**Requires:** `Authorization: Bearer <access_token>`

### Register App

### `POST /api/apps`

### Request

```json
{
  "companyId": "b2c3d4e5-0000-0000-0000-000000000001",
  "name": "FlatPlanet Hub",
  "slug": "flatplanet-hub",
  "baseUrl": "https://hub.flatplanet.io"
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `companyId` | Guid | Yes | Owning company |
| `name` | string | Yes | Display name |
| `slug` | string | Yes | URL-safe identifier. Must be unique. |
| `baseUrl` | string | Yes | App's root URL |

### Success Response — `200`

```json
{
  "id": "c3d4e5f6-0000-0000-0000-000000000001",
  "companyId": "b2c3d4e5-0000-0000-0000-000000000001",
  "name": "FlatPlanet Hub",
  "slug": "flatplanet-hub",
  "baseUrl": "https://hub.flatplanet.io",
  "schemaName": null,
  "status": "active",
  "createdAt": "2026-03-23T10:00:00Z"
}
```

### Error Responses

| Status | Condition |
|---|---|
| `409` | Slug already taken |
| `404` | Company not found |

---

### List / Get / Update App

`GET /api/apps` — list all
`GET /api/apps/{id}` — get one
`PUT /api/apps/{id}` — update (`name`, `baseUrl`)

---

### Update App Status

### `PUT /api/apps/{id}/status`

### Request

```json
{
  "status": "inactive"
}
```

Valid values: `active`, `inactive`

---

### Manage App Users

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/apps/{appId}/users` | List users with access to app |
| `POST` | `/api/apps/{appId}/users` | Grant user access |
| `DELETE` | `/api/apps/{appId}/users/{userId}` | Revoke user access |
| `PUT` | `/api/apps/{appId}/users/{userId}/role` | Change user role in app |

### Grant User Access — Request

```json
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "roleId": "d4e5f6a7-0000-0000-0000-000000000001",
  "expiresAt": null
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `userId` | Guid | Yes | Platform user |
| `roleId` | Guid | Yes | Role to assign |
| `expiresAt` | DateTime? | No | Optional access expiry |

---

## Companies

**Requires:** `Authorization: Bearer <access_token>`

### Create Company

### `POST /api/companies`

### Request

```json
{
  "name": "Acme Corp",
  "slug": "acme-corp",
  "countryCode": "US"
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Company name |
| `slug` | string | Yes | URL-safe unique identifier |
| `countryCode` | string | Yes | ISO 3166-1 alpha-2 (e.g., `US`, `BR`) |

### Error Responses

| Status | Condition |
|---|---|
| `409` | Slug already taken |

Other endpoints: `GET /api/companies`, `GET /api/companies/{id}`, `PUT /api/companies/{id}`, `PUT /api/companies/{id}/status`

---

## Resources

Resources are IAM-managed objects within an app.

**Base path:** `/api/apps/{appId}/resources`
**Requires:** `Authorization: Bearer <access_token>`

### Create Resource

### `POST /api/apps/{appId}/resources`

### Request

```json
{
  "resourceTypeId": "e5f6a7b8-0000-0000-0000-000000000001",
  "name": "Invoice List",
  "identifier": "invoices/list"
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `resourceTypeId` | Guid | Yes | From `GET /api/resource-types` |
| `name` | string | Yes | Display name |
| `identifier` | string | Yes | Unique string identifier used in authorization checks |

### Success Response — `200`

```json
{
  "id": "f6a7b8c9-0000-0000-0000-000000000001",
  "appId": "c3d4e5f6-0000-0000-0000-000000000001",
  "resourceTypeId": "e5f6a7b8-0000-0000-0000-000000000001",
  "resourceTypeName": "Page",
  "name": "Invoice List",
  "identifier": "invoices/list",
  "status": "active",
  "createdAt": "2026-03-23T10:00:00Z"
}
```

### Get Resource Types

### `GET /api/resource-types`

```json
[
  {
    "id": "e5f6a7b8-0000-0000-0000-000000000001",
    "name": "Page",
    "description": "Frontend page or view"
  }
]
```

---

## Admin — Users

**Requires:** `Authorization: Bearer <access_token>` + `manage_users` permission

### List Users

### `GET /api/admin/users`

| Query Param | Type | Required | Description |
|---|---|---|---|
| `search` | string | No | Filters by username or email |
| `isActive` | bool | No | Filter by active status |
| `roleId` | Guid | No | Filter by role |
| `page` | int | No | Default: `1` |
| `pageSize` | int | No | Default: `20` |

### Success Response — `200`

```json
{
  "users": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "gitHubId": 12345678,
      "gitHubUsername": "john-doe",
      "firstName": "John",
      "lastName": "Doe",
      "email": "john@example.com",
      "avatarUrl": "https://avatars.githubusercontent.com/u/12345678",
      "isActive": true,
      "systemRoles": [{ "id": "...", "name": "platform_owner" }],
      "projectMemberships": [
        {
          "projectId": "a1b2c3d4-...",
          "projectName": "Billing Service",
          "projectRole": "admin",
          "permissions": ["read", "write", "ddl"]
        }
      ],
      "createdAt": "2026-01-10T08:00:00Z"
    }
  ],
  "totalCount": 42,
  "page": 1,
  "pageSize": 20
}
```

---

### Create User

### `POST /api/admin/users`

### Request

```json
{
  "gitHubId": 87654321,
  "gitHubUsername": "jane-smith",
  "avatarUrl": "https://avatars.githubusercontent.com/u/87654321",
  "firstName": "Jane",
  "lastName": "Smith",
  "email": "jane@example.com",
  "roleIds": ["d4e5f6a7-0000-0000-0000-000000000001"],
  "projectAssignments": [
    {
      "projectId": "a1b2c3d4-0000-0000-0000-000000000001",
      "projectRoleId": "e5f6a7b8-0000-0000-0000-000000000001"
    }
  ]
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `gitHubId` | long | Yes | GitHub numeric user ID |
| `gitHubUsername` | string | Yes | GitHub login |
| `avatarUrl` | string? | No | Profile avatar |
| `firstName` | string? | No | Display name |
| `lastName` | string? | No | Display name |
| `email` | string? | No | Contact email |
| `roleIds` | Guid[] | No | System roles to assign |
| `projectAssignments` | array | No | Project memberships to assign at creation |

### Error Responses

| Status | Condition |
|---|---|
| `409` | GitHub ID or username already registered |

---

### Bulk Create Users

### `POST /api/admin/users/bulk`

### Request

```json
{
  "users": [
    { ...CreateAdminUserRequest... },
    { ...CreateAdminUserRequest... }
  ]
}
```

Partial failures: the operation continues for valid entries. Invalid entries return errors in the response.

---

### Update User

### `PUT /api/admin/users/{userId}`

### Request

```json
{
  "firstName": "Jane",
  "lastName": "Doe",
  "email": "jane.doe@example.com",
  "isActive": true
}
```

---

### Update User Status

### `PUT /api/admin/users/{userId}/status`

### Request

```json
{
  "status": "suspended"
}
```

Valid values: `active`, `inactive`, `suspended`

### Notes

- Setting status to `inactive` or `suspended` **immediately revokes all active API tokens** for the user
- Active sessions are also terminated

---

### Update User System Roles

### `PUT /api/admin/users/{userId}/roles`

### Request

```json
{
  "roleIds": [
    "d4e5f6a7-0000-0000-0000-000000000001"
  ]
}
```

Replaces the user's current system roles entirely.

---

### Update User App Role

### `PUT /api/admin/users/{userId}/role`

Updates the user's role within a specific app.

### Request

```json
{
  "appId": "c3d4e5f6-0000-0000-0000-000000000001",
  "roleId": "d4e5f6a7-0000-0000-0000-000000000001"
}
```

---

### Update User Project Role

### `PUT /api/admin/users/{userId}/projects/{projectId}/role`

### Request

```json
{
  "projectRoleId": "e5f6a7b8-0000-0000-0000-000000000001"
}
```

---

### Delete User

### `DELETE /api/admin/users/{userId}`

### Error Responses

| Status | Condition |
|---|---|
| `409` | User is a project owner — transfer ownership first |

---

## Admin — Roles & Permissions

**Requires:** `Authorization: Bearer <access_token>` + `manage_roles` permission

### List Roles

### `GET /api/admin/roles`

```json
[
  {
    "id": "d4e5f6a7-0000-0000-0000-000000000001",
    "name": "developer",
    "description": "Standard developer access",
    "permissions": ["read", "write"],
    "isSystem": false,
    "isActive": true,
    "createdAt": "2026-01-01T00:00:00Z"
  }
]
```

---

### Create Custom Role

### `POST /api/admin/roles`

### Request

```json
{
  "name": "reviewer",
  "description": "Read-only code review access",
  "permissions": ["read"]
}
```

### Error Responses

| Status | Condition |
|---|---|
| `409` | Role name already exists |

---

### Update Custom Role

### `PUT /api/admin/roles/{roleId}`

### Request

```json
{
  "name": "senior-reviewer",
  "description": "Updated description",
  "permissions": ["read", "write"]
}
```

All fields optional. System roles (`isSystem: true`) cannot be modified.

### Error Responses

| Status | Condition |
|---|---|
| `403` | Attempting to modify a system role |

---

### Delete Custom Role

### `DELETE /api/admin/roles/{roleId}`

### Error Responses

| Status | Condition |
|---|---|
| `403` | System role — cannot delete |
| `409` | Role is currently assigned to users |

---

### List Permissions

### `GET /api/admin/permissions`

```json
[
  {
    "id": "a7b8c9d0-0000-0000-0000-000000000001",
    "name": "read",
    "description": "Read access to project schema and data",
    "category": "data"
  }
]
```

---

## Audit Log

### `GET /api/audit/auth`

**Requires:** `Authorization: Bearer <access_token>`

| Query Param | Type | Required | Description |
|---|---|---|---|
| `userId` | Guid | No | Filter by user |
| `appId` | Guid | No | Filter by app |
| `eventType` | string | No | e.g., `query.write`, `auth.login` |
| `from` | DateTime | No | Start of time range (ISO 8601) |
| `to` | DateTime | No | End of time range (ISO 8601) |
| `page` | int | No | Default: `1` |
| `pageSize` | int | No | Default: `50` |

### Success Response — `200`

```json
{
  "success": true,
  "data": {
    "logs": [...],
    "totalCount": 150,
    "page": 1,
    "pageSize": 50
  }
}
```

### Notes

- Audit log is **append-only** — records are never deleted
- Logged events include: `auth.login`, `auth.logout`, `query.write`, `migration.create_table`, `migration.alter_table`, `migration.drop_table`

---

## Compliance

**Requires:** `Authorization: Bearer <access_token>`

### Record Consent

### `POST /api/compliance/consent`

### Request

```json
{
  "consentType": "terms_of_service",
  "version": "2.1.0",
  "consented": true
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `consentType` | string | Yes | Consent category (e.g., `terms_of_service`, `privacy_policy`) |
| `version` | string | Yes | Version of the document consented to |
| `consented` | bool | Yes | `true` = granted, `false` = withdrawn |

---

### Get Consent History

### `GET /api/compliance/consent/{userId}`

Returns full consent history for a user.

---

### Report Incident

### `POST /api/compliance/incidents`

### Request

```json
{
  "severity": "high",
  "title": "Unauthorized data access attempt",
  "description": "User attempted to access records outside their tenant",
  "affectedAppId": "c3d4e5f6-0000-0000-0000-000000000001",
  "affectedUsersCount": 1
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `severity` | string | Yes | `low`, `medium`, `high`, `critical` |
| `title` | string | Yes | Short description |
| `description` | string | Yes | Full incident details |
| `affectedAppId` | Guid? | No | App involved |
| `affectedUsersCount` | int? | No | Estimated number of users affected |

---

### Update Incident

### `PUT /api/compliance/incidents/{id}`

### Request

```json
{
  "status": "resolved",
  "resolution": "Access control rule corrected. No data exfiltration confirmed."
}
```

Valid statuses: `open`, `investigating`, `resolved`, `closed`

---

## Claude Config

Returns the generated `CLAUDE.md` and its associated API token for a project.

**Requires:** `Authorization: Bearer <access_token>`

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/projects/{projectId}/claude-config` | Get current config and token |
| `POST` | `/api/projects/{projectId}/claude-config/regenerate` | Rotate token and regenerate config |
| `DELETE` | `/api/projects/{projectId}/claude-config` | Revoke token and clear config |

### Success Response — `200`

```json
{
  "content": "# Project Context\n\nAPI Base URL: https://...\n...",
  "tokenId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "expiresAt": "2026-04-22T10:00:00Z"
}
```

### Notes

- `content` is the full rendered `CLAUDE.md` string — write it directly to disk
- `POST /regenerate` revokes the old token and issues a new one scoped to the same app — update your local `CLAUDE.md` after calling this
- `project.AppId` must be set for this endpoint to work — returns `409` if `AppId` is null

---

## Error Reference

All error responses use the same envelope:

```json
{
  "success": false,
  "data": null,
  "error": "Descriptive error message here"
}
```

Global exception mapping:

| Exception Type | HTTP Status |
|---|---|
| `KeyNotFoundException` | `404 Not Found` |
| `UnauthorizedAccessException` | `403 Forbidden` |
| `ValidationException` | `400 Bad Request` |
| `ArgumentException` | `400 Bad Request` |
| `InvalidOperationException` | `409 Conflict` |
| Unhandled | `500 Internal Server Error` |

### Common Error Scenarios

| Status | When to expect it |
|---|---|
| `400` | Missing required field, invalid enum value, SQL validation failure |
| `401` | No `Authorization` header, expired access token |
| `403` | Valid token but insufficient permission, schema claim invalid, self-registration attempt |
| `404` | Resource with given ID does not exist |
| `409` | Duplicate (username, slug, repo), merge conflict, stale SHA on file update, token reuse |
| `500` | Upstream DB error, GitHub API failure — safe to retry once with backoff |

### Retry Guidance

| Scenario | Action |
|---|---|
| `401` on any request | Refresh the access token, then retry once |
| `401` on refresh | Redirect user to GitHub OAuth login |
| `409` on token refresh | Session may be compromised — force re-login |
| `409` on file upsert | Re-fetch file SHA, update your local state, retry |
| `500` | Retry once after 1–2 seconds. If it persists, do not loop. |
| Any `4xx` except `401` | Do not retry — fix the request |
