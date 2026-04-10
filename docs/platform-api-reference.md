# FlatPlanet Platform API — Frontend Integration Reference

**Version:** 1.1.0
**Base URL:** `https://<your-host>` (local: see `launchSettings.json`)
**API Docs (dev only):** `/scalar`
**Changelog:** [CHANGELOG.md](../CHANGELOG.md)

---

## What's New in v1.1.0

| Change | Details |
|---|---|
| File Storage API | 4 new endpoints under `/api/v1/storage` — upload, list, get SAS URL, soft-delete |
| Azure Blob Storage | Files stored in `flatplanetassets` / `flatplanet-assets` / `{businessCode}/{category}/{fileId}.{ext}` |
| SAS URLs | Time-limited (60 min), generated via Managed Identity user delegation key |

## What's New in v1.0.0

| Change | Details |
|---|---|
| `GET /claude-config/workspace` | New endpoint — returns `CLAUDE-local.md` content (local only, git-ignored) with smart token management |
| `project_type` field | New on all project endpoints — `frontend`, `backend`, `database`, `fullstack` |
| `auth_enabled` field | New on all project endpoints — when `true`, workspace content includes SP auth integration guide |
| Fixed HTTP 500 on `/claude-config` | Removed bad audit log INSERT that violated FK constraint |

---

## Table of Contents

1. [Authentication Overview](#authentication-overview)
2. [Health Check](#health-check)
3. [Auth — Current User](#auth--current-user)
4. [API Tokens](#api-tokens)
   - [Create Token](#create-token)
   - [List Tokens](#list-tokens)
   - [Revoke Token](#revoke-token-1)
5. [Projects](#projects)
   - [List Projects](#list-projects)
   - [Create Project](#create-project)
   - [Get Project](#get-project)
   - [Update Project](#update-project)
   - [Deactivate Project](#deactivate-project)
6. [Project Members](#project-members)
   - [List Members](#list-members)
   - [Add Member](#add-member)
   - [Update Member Role](#update-member-role)
   - [Remove Member](#remove-member)
7. [Claude Config](#claude-config)
   - [Get Config](#get-config)
   - [Regenerate Token](#regenerate-token)
   - [Revoke Token](#revoke-token-2)
   - [Workspace](#workspace)
8. [DB Proxy — Schema](#db-proxy--schema)
   - [List Tables](#list-tables)
   - [Get Columns](#get-columns)
   - [Get Relationships](#get-relationships)
   - [Full Schema](#full-schema)
9. [DB Proxy — Migrations](#db-proxy--migrations)
   - [Create Schema](#create-schema)
   - [Create Table](#create-table)
   - [Alter Table](#alter-table)
   - [Drop Table](#drop-table)
10. [DB Proxy — Queries](#db-proxy--queries)
    - [Read Query](#read-query)
    - [Write Query](#write-query)
11. [File Storage](#file-storage)
    - [Upload File](#upload-file)
    - [List Files](#list-files)
    - [Get File URL](#get-file-url)
    - [Delete File](#delete-file)
12. [Standard Response Envelope](#standard-response-envelope)
13. [Error Reference](#error-reference)

---

## Authentication Overview

All protected endpoints require a JWT in the Authorization header:

```
Authorization: Bearer <access_token>
```

HubApi accepts two token types. The `token_type` JWT claim determines routing:

| Token Type | `token_type` claim | Lifetime | Issued by | Used for |
|---|---|---|---|---|
| Security Platform JWT | *(none)* | 60 min | `flatplanet-security-platform` | Frontend → HubApi |
| HubApi API Token | `api_token` | 30 days | HubApi `/claude-config` or `/api/auth/api-tokens` | Claude Code → DB Proxy |

**Security Platform JWT** — obtained from the Security Platform login flow (not HubApi). Carries claims: `sub`, `email`, `full_name`, `company_id`, `session_id`, and roles.

**HubApi API Token** — generated via `GET /api/projects/{id}/claude-config` or `POST /api/auth/api-tokens`. Carries flat `schema`, `permissions`, and `app_slug` claims. Required for all Schema, Migration, and Query endpoints.

> **Critical:** The DB Proxy endpoints (`/schema`, `/migration`, `/query`) only accept API Tokens — Security Platform JWTs are rejected with `403`. The `ProjectScopeMiddleware` blocks requests before they reach the controller if the token is missing or has an invalid schema claim.

> **JWT claim names:** HubApi sets `MapInboundClaims = false`, so claims are read exactly as they appear in the token — `sub`, `email`, `full_name`, `company_id`, etc. ASP.NET's default mapping (which renames `sub` → `ClaimTypes.NameIdentifier`) is disabled. Use claim names exactly as listed in this document.

---

## Health Check

### `GET /health`

Returns the service health status. No auth required. Used by load balancers and uptime monitors.

**Success Response `200`:**
```json
{
  "status": "Healthy"
}
```

---

## Auth — Current User

### `GET /api/auth/me`

Returns the identity of the currently authenticated user from JWT claims. Makes no database call.

**Auth required:** Security Platform JWT

**Request:** No body.

---

**Success Response `200`:**

```json
{
  "success": true,
  "data": {
    "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "alice@flatplanet.io",
    "fullName": "Alice Martin",
    "companyId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "canCreateProject": true
  }
}
```

**Fields:**

| Field | Type | Description |
|---|---|---|
| `userId` | UUID | User's unique identifier (`sub` claim) |
| `email` | string | User's email address |
| `fullName` | string | Display name from `full_name` claim |
| `companyId` | string or null | Company UUID the user belongs to |
| `canCreateProject` | bool | `true` when the JWT carries a `create_project` permission |

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or invalid JWT |

**Notes:**
- This endpoint reflects JWT claims at issuance time. If the user's roles changed after login, they must re-authenticate to see the update.
- `canCreateProject` is derived from the `permissions` claim — not a DB lookup.

---

## API Tokens

General-purpose long-lived API tokens. These are separate from the project-scoped tokens generated by `/claude-config`. Use these for CI/CD pipelines, integrations, or any tool that needs scoped DB access without going through the full Claude Config flow.

**Auth required for all:** Security Platform JWT

---

### Create Token

### `POST /api/auth/api-tokens`

Issues a new API token with specified permissions.

**Request:**

```json
{
  "name": "CI pipeline token",
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "permissions": ["read", "write"],
  "expiryDays": 30
}
```

**Fields:**

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Human-readable label for this token |
| `appId` | UUID | No | Scope the token to a specific app/project. If omitted, the token is not app-scoped. |
| `permissions` | string[] | Yes | One or more of: `read`, `write`, `ddl` |
| `expiryDays` | int | No | Lifetime in days. Default: `30`. |

---

**Success Response `200`:**

```json
{
  "success": true,
  "data": {
    "tokenId": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
    "token": "eyJhbGci...",
    "name": "CI pipeline token",
    "permissions": ["read", "write"],
    "expiresAt": "2026-04-26T14:00:00Z",
    "mcpConfig": {
      "mcpServers": {
        "flatplanet-db": {
          "command": "npx",
          "args": ["-y", "@flatplanet/mcp-server"],
          "env": {
            "FLATPLANET_API_URL": "https://api.flatplanet.io",
            "FLATPLANET_API_TOKEN": "eyJhbGci..."
          }
        }
      }
    }
  }
}
```

**Fields:**

| Field | Description |
|---|---|
| `tokenId` | UUID — use this for revocation |
| `token` | The raw JWT. Store securely — this is the only time it is returned in plaintext. |
| `mcpConfig` | Ready-to-paste MCP configuration block for Claude Desktop / Claude Code. |

**Error Responses:**

| Code | Reason |
|---|---|
| `400` | Missing `name`, empty `permissions`, or invalid `expiryDays` |
| `401` | Missing or invalid JWT |

---

### List Tokens

### `GET /api/auth/api-tokens`

Returns all active (non-revoked, non-expired) tokens belonging to the authenticated user.

**Request:** No body.

---

**Success Response `200`:**

```json
{
  "success": true,
  "data": [
    {
      "id": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
      "name": "CI pipeline token",
      "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "permissions": ["read", "write"],
      "expiresAt": "2026-04-26T14:00:00Z",
      "lastUsedAt": "2026-03-26T12:00:00Z",
      "createdAt": "2026-03-26T14:00:00Z"
    }
  ]
}
```

**Fields:**

| Field | Type | Description |
|---|---|---|
| `id` | UUID | Token ID for revocation |
| `name` | string | Label assigned at creation |
| `appId` | UUID or null | App scope, if set at creation |
| `permissions` | string[] | Permissions the token carries |
| `expiresAt` | datetime | When the token expires |
| `lastUsedAt` | datetime or null | Last time the token was used for a request |
| `createdAt` | datetime | When the token was issued |

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or invalid JWT |

**Notes:**
- The raw token value is **not** returned here — only metadata. Tokens cannot be recovered after creation.

---

### Revoke Token 1

### `DELETE /api/auth/api-tokens/{tokenId}`

Immediately revokes an API token. Subsequent requests using this token will receive `401`.

**Path Parameters:**

| Param | Type | Description |
|---|---|---|
| `tokenId` | UUID | ID of the token to revoke |

**Request:** No body.

---

**Success Response `200`:**

```json
{
  "success": true
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or invalid JWT |
| `403` | Token does not belong to the authenticated user |
| `404` | Token not found or already revoked |

---

## Projects

### List Projects

### `GET /api/projects`

Returns all projects the authenticated user has access to. Access is determined by the Security Platform — only projects where the user has a role are returned.

**Auth required:** Security Platform JWT

**Request:** No body, no query parameters.

---

**Success Response `200`:**

```json
{
  "success": true,
  "data": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "Acme CRM",
      "description": "Customer relationship management system",
      "schemaName": "project_acme_crm",
      "ownerId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "appSlug": "acme-crm",
      "roleName": "owner",
      "techStack": "React + .NET 10",
      "isActive": true,
      "createdAt": "2026-01-15T10:30:00Z",
      "github": {
        "repoName": "acme-crm",
        "repoFullName": "FlatPlanet-Hub/acme-crm",
        "branch": "main",
        "repoLink": "https://github.com/FlatPlanet-Hub/acme-crm"
      },
      "projectType": "fullstack",
      "authEnabled": false,
      "members": null
    }
  ]
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or invalid JWT |
| `502` | Security Platform unreachable |

**Notes:**
- `members` is always `null` on list responses. Use `GET /api/projects/{id}/members` to fetch members.
- Projects with `appSlug: null` are legacy entries created before the Security Platform migration. These have limited functionality.
- **New users:** If the Security Platform has no record for the user (SP returns `404`), this endpoint returns an empty array rather than erroring.
- **Admin override:** Users with the `view_all_projects` permission on the `dashboard-hub` app receive every project regardless of membership. Their `roleName` is `"admin"` for projects they are not explicitly a member of.

---

### Create Project

### `POST /api/projects`

Creates a new project. Provisions a Postgres schema, registers the app with the Security Platform, creates default roles and permissions (`owner`, `developer`, `viewer`), optionally creates a GitHub repo, and auto-pushes a `CLAUDE.md` to the repo.

**Auth required:** Security Platform JWT with `company_id` claim

**Request — with new GitHub repo:**

```json
{
  "name": "Acme CRM",
  "description": "Customer relationship management system",
  "techStack": "React + .NET 10",
  "github": {
    "createRepo": true,
    "repoName": "acme-crm"
  }
}
```

**Request — link existing repo:**

```json
{
  "name": "Acme CRM",
  "github": {
    "createRepo": false,
    "existingRepoUrl": "https://github.com/FlatPlanet-Hub/acme-crm"
  }
}
```

**Request — no GitHub:**

```json
{
  "name": "Acme CRM",
  "description": "Customer relationship management system",
  "techStack": "React + .NET 10"
}
```

**Fields:**

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Project display name. Used to derive the schema name (`project_{slug}`) and app slug. |
| `description` | string | No | Optional description. |
| `techStack` | string | No | Free-text tech stack. Included in the generated CLAUDE.md. |
| `github` | object | No | GitHub configuration. Omit entirely to skip GitHub integration. |
| `github.createRepo` | bool | Yes (if `github` set) | `true` to create a new repo in the configured org. `false` to link an existing repo. |
| `github.repoName` | string | When `createRepo: true` | Name of the GitHub repo to create. |
| `github.existingRepoUrl` | string | When `createRepo: false` | Full URL of the existing GitHub repo to link. |
| `projectType` | string | No | `"fullstack"` | Project tech stack type. One of: `frontend`, `backend`, `database`, `fullstack` |
| `authEnabled` | boolean | No | `false` | When `true`, workspace content includes SP authentication integration guide |

---

**Success Response `201`:**

```json
{
  "success": true,
  "data": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "Acme CRM",
    "description": "Customer relationship management system",
    "schemaName": "project_acme_crm",
    "ownerId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "appSlug": "acme-crm",
    "roleName": "owner",
    "techStack": "React + .NET 10",
    "isActive": true,
    "createdAt": "2026-03-26T14:00:00Z",
    "github": {
      "repoName": "acme-crm",
      "repoFullName": "FlatPlanet-Hub/acme-crm",
      "branch": "main",
      "repoLink": "https://github.com/FlatPlanet-Hub/acme-crm"
    },
    "projectType": "fullstack",
    "authEnabled": false,
    "members": null
  }
}
```

`github` is `null` in the response when no GitHub configuration was provided.

**Error Responses:**

| Code | Reason |
|---|---|
| `400` | `name` is missing, blank, or `company_id` claim is absent/invalid |
| `401` | Missing or invalid JWT |
| `409` | Project slug already exists, or Security Platform returned an error (SP message included in response body) |
| `502` | Security Platform unreachable |

**Notes:**
- **Creation order:** GitHub repo → Security Platform registration → DB insert. SP failure after GitHub creation will not leave a DB record (no orphans), but the GitHub repo may already exist.
- **CLAUDE.md auto-push:** If a GitHub repo is configured, a `CLAUDE.md` is generated and committed to the repo automatically. This is fire-and-forget — it does not block project creation.
- Project creation makes ~19 sequential Security Platform calls (register app, create 5 permissions, create 3 roles, assign permissions to roles, grant creator `owner`). Expect 2–4 seconds.
- SP errors (e.g. slug conflict `409`) are now surfaced with the real SP error message instead of a generic `500`.

---

### Get Project

### `GET /api/projects/{id}`

Returns a single project by ID.

**Auth required:** Security Platform JWT

**Path Parameters:**

| Param | Type | Description |
|---|---|---|
| `id` | UUID | Project ID |

---

**Success Response `200`:**

```json
{
  "success": true,
  "data": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "Acme CRM",
    "description": "Customer relationship management system",
    "schemaName": "project_acme_crm",
    "ownerId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "appSlug": "acme-crm",
    "roleName": "owner",
    "techStack": "React + .NET 10",
    "isActive": true,
    "createdAt": "2026-01-15T10:30:00Z",
    "github": {
      "repoName": "acme-crm",
      "repoFullName": "FlatPlanet-Hub/acme-crm",
      "branch": "main",
      "repoLink": "https://github.com/FlatPlanet-Hub/acme-crm"
    },
    "projectType": "fullstack",
    "authEnabled": false,
    "members": null
  }
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or invalid JWT |
| `403` | User does not have access to this project |
| `404` | Project not found |

**Notes:**
- `projectType` — one of `frontend`, `backend`, `database`, `fullstack`. Controls which tech stack standards are injected into `CLAUDE-local.md`.
- `authEnabled` — when `true`, `CLAUDE-local.md` workspace content includes the SP authentication integration guide.
- **Admin override:** Users with the `view_all_projects` permission on the `dashboard-hub` app bypass the Security Platform authorization check and can retrieve any project. Their `roleName` is `"admin"` if they are not an explicit member of the project.

---

### Update Project

### `PUT /api/projects/{id}`

Updates project metadata. Requires `manage_members` permission (checked via Security Platform).

**Auth required:** Security Platform JWT with `manage_members` on this project

**Path Parameters:**

| Param | Type | Description |
|---|---|---|
| `id` | UUID | Project ID |

**Request:**

```json
{
  "name": "Acme CRM v2",
  "description": "Updated description",
  "techStack": "Next.js + .NET 10"
}
```

**Fields:**

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | No | New display name |
| `description` | string | No | New description |
| `techStack` | string | No | Free-text tech stack description |
| `projectType` | string | No | Update project type (optional) |
| `authEnabled` | boolean | No | Update auth enabled flag (optional) |

All fields are optional. Only provided fields are updated.

---

**Success Response `200`:** Returns updated `ProjectResponse` (same shape as [Get Project](#get-project)).

**Error Responses:**

| Code | Reason |
|---|---|
| `400` | Invalid input |
| `401` | Missing or invalid JWT |
| `403` | User lacks `manage_members` on this project |
| `404` | Project not found |

---

### Deactivate Project

### `DELETE /api/projects/{id}`

Soft-deactivates a project. Sets `isActive = false`. Requires `delete_project` permission (checked via Security Platform).

**Auth required:** Security Platform JWT with `delete_project` on this project

**Path Parameters:**

| Param | Type | Description |
|---|---|---|
| `id` | UUID | Project ID |

**Request:** No body.

---

**Success Response `200`:**

```json
{
  "success": true
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or invalid JWT |
| `403` | User lacks `delete_project` on this project |
| `404` | Project not found |

**Notes:**
- This is a soft delete only — `isActive` is set to `false`. Data is not removed.
- Deactivated projects no longer appear in `GET /api/projects`.
- Existing API tokens for this project remain valid until their natural expiry. Revoke them explicitly via `DELETE /api/projects/{id}/claude-config` first.

---

## Project Members

### List Members

### `GET /api/projects/{id}/members`

Returns all members of a project with their roles and permissions. Data is fetched from the Security Platform.

**Auth required:** Security Platform JWT

**Path Parameters:**

| Param | Type | Description |
|---|---|---|
| `id` | UUID | Project ID |

---

**Success Response `200`:**

```json
{
  "success": true,
  "data": [
    {
      "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "githubUsername": null,
      "fullName": "Alice Martin",
      "email": "alice@flatplanet.io",
      "roleName": "owner",
      "permissions": ["read", "write", "ddl", "manage_members", "delete_project"],
      "grantedAt": "2026-01-15T10:30:00Z"
    },
    {
      "userId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
      "githubUsername": "bob-dev",
      "fullName": "Bob Chen",
      "email": "bob@flatplanet.io",
      "roleName": "developer",
      "permissions": ["read", "write", "ddl"],
      "grantedAt": "2026-02-01T09:00:00Z"
    }
  ]
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or invalid JWT |
| `403` | User does not have access to this project |
| `404` | Project not found |
| `502` | Security Platform unreachable |

---

### Add Member

### `POST /api/projects/{id}/members`

Grants a user access to the project with a specified role. Optionally adds them as a GitHub repo collaborator.

**Auth required:** Security Platform JWT with `manage_members` on this project

**Path Parameters:**

| Param | Type | Description |
|---|---|---|
| `id` | UUID | Project ID |

**Request:**

```json
{
  "userId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "role": "developer",
  "githubUsername": "bob-dev"
}
```

**Fields:**

| Field | Type | Required | Description |
|---|---|---|---|
| `userId` | UUID | Yes | Security Platform user ID to grant access to |
| `role` | string | Yes | One of: `owner`, `developer`, `viewer` |
| `githubUsername` | string | No | If provided, adds the user as a GitHub repo collaborator. Role → GitHub permission: `owner` → `admin`, `developer` → `push`, `viewer` → `pull`. |

---

**Success Response `200`:**

```json
{
  "success": true
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `400` | Invalid role name or missing required field |
| `401` | Missing or invalid JWT |
| `403` | Caller lacks `manage_members` on this project |
| `404` | Project not found |
| `409` | User already has a role on this project |
| `502` | Security Platform unreachable |

**Notes:**
- **dashboard-hub auto-grant:** After granting the project role, HubApi checks whether the user has any role on the `dashboard-hub` app. If not, they are automatically granted `viewer` on `dashboard-hub`. This is fire-and-forget — a failure is logged but does not roll back the project role grant.
- GitHub collaborator invitation is fire-and-forget — a GitHub failure does not roll back the role grant.
- `githubUsername` is only used at invite time. If skipped, there is no later endpoint to add the user to the GitHub repo without removing and re-adding the member.
- The user must already exist in the Security Platform. This endpoint does not create users.

---

### Update Member Role

### `PUT /api/projects/{id}/members/{userId}/role`

Changes an existing member's role.

**Auth required:** Security Platform JWT with `manage_members` on this project

**Path Parameters:**

| Param | Type | Description |
|---|---|---|
| `id` | UUID | Project ID |
| `userId` | UUID | The user whose role is being changed |

**Request:**

```json
{
  "role": "viewer"
}
```

**Fields:**

| Field | Type | Required | Description |
|---|---|---|---|
| `role` | string | Yes | New role: `owner`, `developer`, or `viewer` |

---

**Success Response `200`:**

```json
{
  "success": true
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `400` | Invalid role name |
| `401` | Missing or invalid JWT |
| `403` | Caller lacks `manage_members` |
| `404` | Project not found, or user is not a member |
| `502` | Security Platform unreachable |

**Notes:**
- GitHub repo permissions are **not updated** on role change (known limitation — the Security Platform does not expose GitHub identity). Updated permissions will be reflected on the next `claude-config` regeneration.

---

### Remove Member

### `DELETE /api/projects/{id}/members/{userId}`

Removes a user from the project, revokes their Security Platform role, and revokes any API tokens they held for this project.

**Auth required:** Security Platform JWT with `manage_members` on this project

**Path Parameters:**

| Param | Type | Description |
|---|---|---|
| `id` | UUID | Project ID |
| `userId` | UUID | The user to remove |

**Request:** No body.

---

**Success Response `200`:**

```json
{
  "success": true
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or invalid JWT |
| `403` | Caller lacks `manage_members` |
| `404` | Project not found, or user is not a member |
| `502` | Security Platform unreachable |

**Notes:**
- API tokens belonging to this user for this project are immediately revoked.
- GitHub repo collaborator access is **not removed** (known limitation).

---

## Claude Config

Manages the CLAUDE.md context file and the long-lived API token that Claude Code uses to access the DB Proxy. One token per project — regenerating replaces the previous one.

**Auth required for all:** Security Platform JWT

---

### Get Config

### `GET /api/projects/{id}/claude-config`

Generates a `CLAUDE.md` file content and a 30-day API token for Claude Code. If a valid token already exists, it is reused.

**Path Parameters:**

| Param | Type | Description |
|---|---|---|
| `id` | UUID | Project ID |

**Request:** No body.

---

**Success Response `200`:**

```json
{
  "success": true,
  "data": {
    "content": "# Project: Acme CRM\n\n## DB Proxy\n...",
    "tokenId": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
    "expiresAt": "2026-04-26T14:00:00Z"
  }
}
```

**Fields:**

| Field | Description |
|---|---|
| `content` | Full CLAUDE.md file text. Write this to `.claude/CLAUDE.md` in the project repo. |
| `tokenId` | UUID of the issued token (used for tracking / revocation). |
| `expiresAt` | Token expiry — 30 days from issuance. |

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or invalid JWT |
| `403` | User does not have access to this project |
| `404` | Project not found |
| `409` | Project has no `appSlug` — legacy project not registered with Security Platform |

---

### Regenerate Token

### `POST /api/projects/{id}/claude-config/regenerate`

Revokes the current API token and issues a new one. Returns fresh CLAUDE.md content with the new token embedded.

**Path Parameters:**

| Param | Type | Description |
|---|---|---|
| `id` | UUID | Project ID |

**Request:** No body.

---

**Success Response `200`:** Same shape as [Get Config](#get-config).

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or invalid JWT |
| `403` | User does not have access to this project |
| `404` | Project not found |
| `409` | Project has no `appSlug` — legacy project not registered with Security Platform |

**Notes:**
- The old token is immediately invalidated. Any Claude Code session using the old token will receive `401` on the next request.
- After regeneration, update `.claude/CLAUDE.md` in the project repo with the new `content`.

---

### Revoke Token 2

### `DELETE /api/projects/{id}/claude-config`

Revokes the active API token for this project without issuing a replacement.

**Path Parameters:**

| Param | Type | Description |
|---|---|---|
| `id` | UUID | Project ID |

**Request:** No body.

---

**Success Response `200`:**

```json
{
  "success": true
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or invalid JWT |
| `403` | User does not have access to this project |
| `404` | Project not found |
| `409` | Project has no `appSlug` — legacy project not registered with Security Platform |

---

### Workspace

#### `GET /api/projects/{id}/claude-config/workspace`

Generates `CLAUDE-local.md` content for local Claude Code use. The file is **local only** — it must be added to `.gitignore` and never committed, as it contains a live API token.

**Smart token logic:** If an active API token already exists for this user and project, it is revoked and a new one is issued. This prevents silent token accumulation.

**Auth:** Security Platform JWT required.

**Response `200`:**
```json
{
  "success": true,
  "data": {
    "content": "# Project Context\n...",
    "filename": "CLAUDE-local.md",
    "gitignoreEntry": "CLAUDE-local.md",
    "tokenId": "310a2e03-eda8-4bb3-9fcc-352eeb821b70",
    "expiresAt": "2026-05-06T04:39:26Z"
  }
}
```

| Field | Type | Description |
|---|---|---|
| `content` | string | Full `CLAUDE-local.md` file content — write this to disk |
| `filename` | string | Always `"CLAUDE-local.md"` |
| `gitignoreEntry` | string | Add this string to your `.gitignore` |
| `tokenId` | uuid | ID of the newly issued API token |
| `expiresAt` | datetime | Token expiry (30 days from generation) |

**Errors:** `401` no auth, `403` no project access, `404` project not found.

> **Frontend note:** After calling this endpoint, write `content` to `{localProjectPath}/CLAUDE-local.md` and ensure `CLAUDE-local.md` is in `.gitignore`. The browser cannot write files directly — trigger a download or use the File System Access API.

---

## DB Proxy — Schema

All schema endpoints require an **API Token** (`token_type: api_token`). Security Platform JWTs return `403`.

The token's `schema` claim determines which Postgres schema is queried. Do not prefix table names in queries — `search_path` is set automatically.

---

### List Tables

### `GET /api/projects/{projectId}/schema/tables`

Returns all tables in the project's Postgres schema.

**Required permission:** `read`

---

**Success Response `200`:**

```json
{
  "success": true,
  "data": [
    { "tableName": "customers", "tableType": "BASE TABLE" },
    { "tableName": "orders", "tableType": "BASE TABLE" }
  ]
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or expired token |
| `403` | Token lacks `read` permission, or schema claim is missing/invalid |

---

### Get Columns

### `GET /api/projects/{projectId}/schema/columns`

Returns column definitions. Optionally filter by table name.

**Required permission:** `read`

**Query Parameters:**

| Param | Type | Required | Description |
|---|---|---|---|
| `table` | string | No | Table name to filter by. Omit to return all columns in the schema. |

---

**Success Response `200`:**

```json
{
  "success": true,
  "data": [
    {
      "tableName": "customers",
      "columnName": "id",
      "dataType": "uuid",
      "isNullable": false,
      "columnDefault": "gen_random_uuid()",
      "ordinalPosition": 1
    },
    {
      "tableName": "customers",
      "columnName": "email",
      "dataType": "text",
      "isNullable": false,
      "columnDefault": null,
      "ordinalPosition": 2
    }
  ]
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or expired token |
| `403` | Token lacks `read` permission |

---

### Get Relationships

### `GET /api/projects/{projectId}/schema/relationships`

Returns all foreign key relationships in the project schema.

**Required permission:** `read`

---

**Success Response `200`:**

```json
{
  "success": true,
  "data": [
    {
      "constraintName": "orders_customer_id_fkey",
      "tableName": "orders",
      "columnName": "customer_id",
      "foreignTableName": "customers",
      "foreignColumnName": "id"
    }
  ]
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or expired token |
| `403` | Token lacks `read` permission |

---

### Full Schema

### `GET /api/projects/{projectId}/schema/full`

Returns tables, columns, and relationships in a single call. Use this to build a complete data dictionary.

**Required permission:** `read`

---

**Success Response `200`:**

```json
{
  "success": true,
  "data": {
    "tables": [
      { "tableName": "customers", "tableType": "BASE TABLE" }
    ],
    "columns": [
      {
        "tableName": "customers",
        "columnName": "id",
        "dataType": "uuid",
        "isNullable": false,
        "columnDefault": "gen_random_uuid()",
        "ordinalPosition": 1
      }
    ],
    "relationships": [
      {
        "constraintName": "orders_customer_id_fkey",
        "tableName": "orders",
        "columnName": "customer_id",
        "foreignTableName": "customers",
        "foreignColumnName": "id"
      }
    ]
  }
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or expired token |
| `403` | Token lacks `read` permission |

---

## DB Proxy — Migrations

All migration endpoints require an **API Token** with `ddl` permission.

After every DDL operation, HubApi syncs `DATA_DICTIONARY.md` to the project's GitHub repo (fire-and-forget — a GitHub failure never rolls back a successful DDL).

All migration endpoints return `200` with no payload on success (`{ "success": true }`).

---

### Create Schema

### `POST /api/projects/{projectId}/migration/create-schema`

Initializes the project's Postgres schema. Run this once after project creation, before creating any tables.

**Required permission:** `ddl`

**Request:** No body.

---

**Success Response `200`:**

```json
{
  "success": true
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `401` | Missing or expired token |
| `403` | Token lacks `ddl` permission or schema claim is invalid |

**Notes:**
- This is idempotent in practice (`CREATE SCHEMA IF NOT EXISTS`) — safe to call if you are unsure whether the schema exists.

---

### Create Table

### `POST /api/projects/{projectId}/migration/create-table`

Creates a new table in the project schema.

**Required permission:** `ddl`

**Request:**

```json
{
  "tableName": "customers",
  "columns": [
    {
      "name": "id",
      "type": "uuid",
      "isPrimaryKey": true,
      "default": "gen_random_uuid()",
      "nullable": false
    },
    {
      "name": "email",
      "type": "text",
      "isPrimaryKey": false,
      "default": null,
      "nullable": false
    },
    {
      "name": "created_at",
      "type": "timestamptz",
      "isPrimaryKey": false,
      "default": "now()",
      "nullable": false
    }
  ],
  "enableRls": true
}
```

**Fields:**

| Field | Type | Required | Description |
|---|---|---|---|
| `tableName` | string | Yes | Table name. Must be a valid SQL identifier (lowercase, no spaces, no reserved words). |
| `columns` | array | Yes | At least one column required. |
| `columns[].name` | string | Yes | Column name. Same naming rules as `tableName`. |
| `columns[].type` | string | Yes | Postgres type: `uuid`, `text`, `int`, `bigint`, `boolean`, `timestamptz`, `numeric`, `jsonb`, etc. |
| `columns[].isPrimaryKey` | bool | Yes | Set `true` for the primary key column. |
| `columns[].default` | string | No | SQL default expression, e.g. `gen_random_uuid()`, `now()`. |
| `columns[].nullable` | bool | Yes | Whether the column allows `NULL`. Default: `true`. |
| `enableRls` | bool | Yes | Enables Row Level Security. Recommended: `true`. |

---

**Success Response `200`:**

```json
{
  "success": true
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `400` | Invalid `tableName`, invalid column `name`, or no columns provided |
| `401` | Missing or expired token |
| `403` | Token lacks `ddl` permission |
| `409` | Table already exists |

**Notes:**
- Table and column names are validated server-side. SQL keywords and special characters are rejected.
- `enableRls: true` enables RLS on the table but does not configure any policies. Policies must be added via `POST /query/write` or a manual migration.

---

### Alter Table

### `PUT /api/projects/{projectId}/migration/alter-table`

Modifies an existing table's columns.

**Required permission:** `ddl`

**Request:**

```json
{
  "tableName": "customers",
  "operations": [
    {
      "type": "AddColumn",
      "columnName": "phone",
      "dataType": "text",
      "nullable": true
    },
    {
      "type": "RenameColumn",
      "columnName": "email",
      "newColumnName": "email_address"
    },
    {
      "type": "DropColumn",
      "columnName": "legacy_field"
    }
  ]
}
```

**Fields:**

| Field | Type | Required | Description |
|---|---|---|---|
| `tableName` | string | Yes | Target table |
| `operations` | array | Yes | One or more operations, applied in order |
| `operations[].type` | string | Yes | One of: `AddColumn`, `DropColumn`, `RenameColumn`, `SetNotNull`, `DropNotNull` |
| `operations[].columnName` | string | Yes | Column to act on |
| `operations[].newColumnName` | string | Conditional | Required when `type` is `RenameColumn` |
| `operations[].dataType` | string | Conditional | Required when `type` is `AddColumn` |
| `operations[].nullable` | bool | Conditional | Used with `AddColumn` |

**Operation Types:**

| Type | Effect |
|---|---|
| `AddColumn` | Adds a new column |
| `DropColumn` | Drops a column — **irreversible** |
| `RenameColumn` | Renames a column |
| `SetNotNull` | Adds `NOT NULL` — fails if existing rows contain nulls |
| `DropNotNull` | Removes `NOT NULL` constraint |

---

**Success Response `200`:**

```json
{
  "success": true
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `400` | Invalid `tableName`, `columnName`, or `newColumnName` |
| `401` | Missing or expired token |
| `403` | Token lacks `ddl` permission |
| `404` | Table or column does not exist |
| `500` | `SetNotNull` on a column that contains null values — Postgres raises an error, propagated as 500 |

**Notes:**
- Operations are applied in the order given. If one fails, subsequent operations in the same request are not applied.
- `DropColumn` is irreversible.

---

### Drop Table

### `DELETE /api/projects/{projectId}/migration/drop-table`

Drops a table from the project schema. Irreversible — all data is permanently deleted.

**Required permission:** `ddl`

**Query Parameters:**

| Param | Type | Required | Description |
|---|---|---|---|
| `table` | string | Yes | Name of the table to drop |

**Request:** No body.

---

**Success Response `200`:**

```json
{
  "success": true
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `400` | Missing or invalid `table` parameter |
| `401` | Missing or expired token |
| `403` | Token lacks `ddl` permission |
| `404` | Table does not exist |

**Notes:**
- Hard `DROP TABLE`. No soft delete or confirmation prompt.
- Drop dependent tables first, or handle foreign key constraints manually before dropping.

---

## DB Proxy — Queries

All query endpoints require an **API Token**. Security Platform JWTs are rejected.

---

### Read Query

### `POST /api/projects/{projectId}/query/read`

Executes a parameterized `SELECT` query against the project schema.

**Required permission:** `read`

**Request:**

```json
{
  "sql": "SELECT id, email, created_at FROM customers WHERE created_at > @since ORDER BY created_at DESC LIMIT 50",
  "parameters": {
    "since": "2026-01-01T00:00:00Z"
  }
}
```

**Fields:**

| Field | Type | Required | Description |
|---|---|---|---|
| `sql` | string | Yes | A `SELECT` statement. DDL and DML keywords are blocked at the middleware level. |
| `parameters` | object | No | Key-value pairs. Keys must match `@param` placeholders in `sql`. |

---

**Success Response `200`:**

```json
{
  "success": true,
  "data": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "email": "alice@flatplanet.io",
      "created_at": "2026-03-01T10:00:00Z"
    }
  ]
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `400` | SQL contains blocked keywords (`INSERT`, `UPDATE`, `DELETE`, `DROP`, `CREATE`, `ALTER`, `TRUNCATE`) |
| `401` | Missing or expired token |
| `403` | Token lacks `read` permission |
| `500` | SQL syntax error or query execution failure — Postgres exceptions are unhandled and propagate as 500 |

**Notes:**
- `search_path` is automatically set to the project schema before execution — do not prefix table names with the schema name.
- Read queries are **not** audit logged.
- No built-in query timeout. Use `LIMIT` to bound result sets.

---

### Write Query

### `POST /api/projects/{projectId}/query/write`

Executes a parameterized `INSERT`, `UPDATE`, or `DELETE` against the project schema.

**Required permission:** `write`

**Request:**

```json
{
  "sql": "INSERT INTO customers (email, created_at) VALUES (@email, @created_at)",
  "parameters": {
    "email": "bob@flatplanet.io",
    "created_at": "2026-03-26T14:00:00Z"
  }
}
```

**Fields:**

| Field | Type | Required | Description |
|---|---|---|---|
| `sql` | string | Yes | An `INSERT`, `UPDATE`, or `DELETE`. DDL keywords are blocked. |
| `parameters` | object | No | Parameterized values matching `@param` placeholders. |

---

**Success Response `200`:**

```json
{
  "success": true,
  "rowsAffected": 1
}
```

**Error Responses:**

| Code | Reason |
|---|---|
| `400` | SQL contains blocked DDL keywords |
| `401` | Missing or expired token |
| `403` | Token lacks `write` permission |
| `500` | SQL syntax error, constraint violation, or execution failure — Postgres exceptions propagate as 500 |

**Notes:**
- `rowsAffected` reflects the number of rows changed.
- Write operations **are** audit logged.
- No transaction wrapping — each request is a single statement.
- DDL is blocked (`CREATE`, `DROP`, `ALTER`, `TRUNCATE`). Use the Migration endpoints for schema changes.

---

## File Storage

Centralized file storage using Azure Blob Storage. Files are scoped per business code and category.

**Auth required**: Yes — Security Platform JWT (user must have a valid `business_codes` claim matching the requested `businessCode`).

**Storage layout**: `flatplanetassets` (account) / `flatplanet-assets` (container) / `{businessCode}/{category}/{fileId}.{ext}`

---

### Upload File

**`POST /api/v1/storage/upload`**

Uploads a file to Azure Blob Storage. Request must be `multipart/form-data`. Maximum file size is **50 MB**.

#### Request — multipart/form-data

| Field | Type | Required | Notes |
|---|---|---|---|
| `file` | binary | Yes | The file to upload. |
| `businessCode` | string | Yes | Short company code (e.g. `"fp"`). Must match a code in the caller's `business_codes` JWT claim. |
| `category` | string | No | Logical grouping for the file (e.g. `"logos"`, `"documents"`). Defaults to `"general"`. |
| `tags` | string | No | Comma-separated tag list (e.g. `"logo,primary"`). |

#### Success Response — 200

```json
{
  "fileId": "7ea2a19e-...",
  "businessCode": "fp",
  "category": "general",
  "originalName": "logo.png",
  "contentType": "image/png",
  "fileSizeBytes": 48210,
  "tags": ["logo", "primary"],
  "sasUrl": "https://flatplanetassets.blob.core.windows.net/...",
  "sasExpiresAt": "2026-04-10T02:13:27Z",
  "createdAt": "2026-04-10T01:13:25Z"
}
```

| Field | Type | Notes |
|---|---|---|
| `fileId` | UUID | Unique identifier for the file. Use this in subsequent URL / delete calls. |
| `businessCode` | string | Business code the file is scoped to. |
| `category` | string | Category the file was stored under. |
| `originalName` | string | Original filename from the upload. |
| `contentType` | string | MIME type detected from the upload. |
| `fileSizeBytes` | integer | File size in bytes. |
| `tags` | string[] | Tags parsed from the comma-separated upload field. |
| `sasUrl` | string | Time-limited Azure Blob SAS URL. Valid for 60 minutes from upload. |
| `sasExpiresAt` | string | ISO 8601 expiry timestamp for the SAS URL. |
| `createdAt` | string | ISO 8601 timestamp of upload. |

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `400` | — | Missing `file` or `businessCode` field; file exceeds 50 MB limit. |
| `403` | — | Caller's JWT `business_codes` claim does not include the requested `businessCode`. |

---

### List Files

**`GET /api/v1/storage/files`**

Returns files accessible to the caller, with optional filters.

#### Query Parameters

| Parameter | Type | Required | Notes |
|---|---|---|---|
| `businessCode` | string | No | Filter by business code (e.g. `fp`). |
| `category` | string | No | Filter by category (e.g. `logos`). |
| `tags` | string | No | Comma-separated tag filter — returns files that have **all** specified tags. |

#### Example Request

```
GET /api/v1/storage/files?businessCode=fp&category=logos&tags=test
```

#### Success Response — 200

```json
{
  "success": true,
  "data": [
    {
      "fileId": "7ea2a19e-...",
      "businessCode": "fp",
      "category": "logos",
      "originalName": "logo.png",
      "contentType": "image/png",
      "fileSizeBytes": 48210,
      "tags": ["logo", "primary"],
      "createdAt": "2026-04-10T01:13:25Z"
    }
  ]
}
```

#### Notes

- Results are filtered to files belonging to `businessCode` values in the caller's JWT `business_codes` claim.
- Soft-deleted files are excluded.

---

### Get File URL

**`GET /api/v1/storage/files/{fileId}/url`**

Generates a fresh SAS URL for an existing file. The previous URL (from upload or a prior call) does not need to be revoked — SAS URLs are stateless.

#### Success Response — 200

```json
{
  "sasUrl": "https://flatplanetassets.blob.core.windows.net/...",
  "expiresAt": "2026-04-10T02:30:30Z"
}
```

| Field | Type | Notes |
|---|---|---|
| `sasUrl` | string | New time-limited SAS URL. Valid for 60 minutes. |
| `expiresAt` | string | ISO 8601 expiry timestamp. |

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `403` | — | File belongs to a `businessCode` not in the caller's JWT claim. |
| `404` | — | No file with that ID, or the file has been soft-deleted. |

#### Notes

- SAS URLs are generated using Managed Identity user delegation keys. No storage account key is stored in the application.
- Call this endpoint to refresh a URL before it expires (60-minute window).

---

### Delete File

**`DELETE /api/v1/storage/files/{fileId}`**

Soft-deletes a file. The database record is marked deleted; the blob in Azure is no longer accessible through the API. The blob itself may be retained in storage for a configurable retention period.

#### Success Response — 200

```json
{ "success": true, "message": "File deleted." }
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `403` | — | File belongs to a `businessCode` not in the caller's JWT claim. |
| `404` | — | No file with that ID, or already deleted. |

---

## Standard Response Envelope

All endpoints return a consistent envelope. **Null fields are omitted from the serialized response** — do not expect `"data": null` or `"error": null` to be present.

**Success with data:**
```json
{
  "success": true,
  "data": { ... }
}
```

**Success with no data (void operations):**
```json
{
  "success": true
}
```

**Success with rows affected (write queries):**
```json
{
  "success": true,
  "rowsAffected": 1
}
```

**Error:**
```json
{
  "success": false,
  "error": "Human-readable message"
}
```

| Field | Present when | Description |
|---|---|---|
| `success` | Always | `true` on success, `false` on failure |
| `data` | Success + has payload | Response data. **Omitted** when the operation returns no value. |
| `rowsAffected` | Write query success | Number of rows affected. **Omitted** on all other responses. |
| `error` | Failure | Human-readable error message. Do not parse this string — treat it as opaque. **Omitted** on success. |

---

## Error Reference

| Code | Meaning | Common Causes |
|---|---|---|
| `400` | Bad Request | Missing required field, validation failure, blocked SQL keyword |
| `401` | Unauthorized | Missing `Authorization` header, expired JWT, revoked API token |
| `403` | Forbidden | Valid token but insufficient permission; Security Platform denied the action; invalid or missing schema claim on API token |
| `404` | Not Found | Project, member, or resource does not exist; or the caller has no visibility into it |
| `409` | Conflict | Duplicate project slug; user already a member; `InvalidOperationException` in a service (e.g. project has no `appSlug`) |
| `500` | Server Error | Unhandled exception — Postgres errors (syntax, constraint violations, `SetNotNull` on nullable data) propagate as 500 |
| `502` | Bad Gateway | Security Platform unreachable or returned an unexpected error |

Error code mapping is driven by `GlobalExceptionMiddleware`:

| Exception type | HTTP code |
|---|---|
| `KeyNotFoundException` | 404 |
| `UnauthorizedAccessException` | 403 |
| `ValidationException` / `ArgumentException` | 400 |
| `InvalidOperationException` | 409 |
| Anything else (incl. Postgres errors) | 500 |

**On `403` from DB Proxy endpoints:** Almost always means the API token's `permissions` claim does not include the required permission (e.g., running a write query with a `read`-only token). Regenerate the token via `POST /api/projects/{id}/claude-config/regenerate`.

**On `500` from query/migration endpoints:** Check your SQL for syntax errors and verify column types and constraints before retrying.

**On `502`:** The Security Platform is a hard dependency for project creation, member management, and permission checks. Schema, Migration, and Query endpoints are unaffected — they rely on API tokens only.
