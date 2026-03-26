# FlatPlanet Hub — Feature 3: Projects & Member Management

## What This Is
Projects are the core unit of HubApi. A project maps to:
- A **GitHub repository** (private, under FlatPlanet-Hub org) — created by the frontend before registering in HubApi
- An isolated **Postgres schema** (`project_abc123`) — created by HubApi on registration
- An **app** in the Security Platform — registered by HubApi on project creation for RBAC

Non-technical users create projects in the frontend. They see only the projects they are allowed onto. Claude Code does all the actual coding.

## What Was Removed
The following no longer exist in HubApi:
- `AdminUserController` — user management is owned by Security Platform
- `AdminRoleController` — role management is owned by Security Platform
- `AdminPermissionController` — permission management is owned by Security Platform
- `CompaniesController` — companies are owned by Security Platform
- `AppsController` — app registration happens automatically on project creation
- Project-level role CRUD (`/api/projects/{id}/roles`) — roles are managed in Security Platform

---

## Project Creation Flow

The **frontend** handles GitHub:
1. User clicks "Create Project" in the frontend
2. Frontend checks user is a GitHub org member (FlatPlanet-Hub)
3. Frontend creates the private GitHub repo via GitHub API
4. Frontend calls `POST /api/projects` with the repo details

HubApi handles the rest:
1. Creates an isolated Postgres schema (`project_{slug}`)
2. Registers the project as an **app** in Security Platform (`POST /security-platform/api/v1/apps`)
3. Grants the creator the `owner` role in Security Platform
4. Seeds `DATA_DICTIONARY.md` in the repo (via GitHub service token)
5. Returns the project record

---

## API Endpoints

All endpoints require a valid Security Platform JWT.

### Projects

- `GET /api/projects` — List projects this user can see

  Backend: calls Security Platform to get user's `user_app_roles` → returns only projects where user has an active role.

  Response:
  ```json
  {
    "success": true,
    "data": [
      {
        "id": "project-uuid",
        "name": "Customer Portal",
        "description": "CRM for Sydney office",
        "schemaName": "project_abc123",
        "githubRepo": "FlatPlanet-Hub/customer-portal",
        "status": "active",
        "role": "owner",
        "createdAt": "2026-03-01T00:00:00Z"
      }
    ]
  }
  ```

- `POST /api/projects` — Register a new project (requires `create_project` permission)
  ```json
  {
    "name": "Customer Portal",
    "description": "CRM for Sydney office",
    "githubRepo": "FlatPlanet-Hub/customer-portal",
    "techStack": "Next.js, TypeScript, PostgreSQL"
  }
  ```
  Backend steps:
  1. Generate `schemaName` from slug: `project_{sanitized_name}`
  2. Create isolated Postgres schema
  3. Register app in Security Platform
  4. Grant caller `owner` role via Security Platform
  5. Return project record

- `GET /api/projects/{id}` — Get project detail (user must have access)

- `PUT /api/projects/{id}` — Update project name/description (requires `owner` role)
  ```json
  {
    "name": "Customer Portal v2",
    "description": "Updated CRM",
    "techStack": "Next.js, TypeScript, PostgreSQL"
  }
  ```

- `DELETE /api/projects/{id}` — Deactivate project (requires `owner` role)

---

### Project Members

Who can invite: users with `manage_members` permission on the project (owners by default).

- `GET /api/projects/{id}/members` — List all members and their roles
  ```json
  {
    "success": true,
    "data": [
      {
        "userId": "user-uuid",
        "fullName": "Chris Moriarty",
        "email": "chris@example.com",
        "githubUsername": "Chris-Moriarty",
        "role": "developer",
        "permissions": ["read", "write", "ddl"],
        "grantedAt": "2026-03-01T00:00:00Z"
      }
    ]
  }
  ```

- `POST /api/projects/{id}/members` — Add a user to the project
  ```json
  {
    "userId": "user-uuid",
    "role": "developer"
  }
  ```
  Backend steps:
  1. Validate caller has `manage_members`
  2. Grant role in Security Platform (`POST /security-platform/api/v1/apps/{appId}/users`)
  3. Add user as GitHub repo collaborator via service token with mapped GitHub permission
  4. Return updated member list

  GitHub permission mapping:
  | Project Role | GitHub Permission |
  |---|---|
  | `viewer` | `pull` |
  | `developer` | `push` |
  | `owner` | `admin` |

- `PUT /api/projects/{id}/members/{userId}` — Change a member's role
  ```json
  { "role": "viewer" }
  ```
  Backend: updates role in Security Platform + updates GitHub collaborator permission.

- `DELETE /api/projects/{id}/members/{userId}` — Remove a member
  Backend:
  1. Revokes role in Security Platform
  2. Removes GitHub collaborator access via service token
  3. Revokes any active CLAUDE.md API tokens for that user on this project

---

## Permissions Model

Project roles and their DB proxy permissions:

| Role | `read` | `write` | `ddl` | `manage_members` |
|------|--------|---------|-------|-----------------|
| `owner` | ✓ | ✓ | ✓ | ✓ |
| `developer` | ✓ | ✓ | ✓ | |
| `viewer` | ✓ | | | |

These roles are created in Security Platform when the project is registered. The CLAUDE.md API token carries only the permissions the user's role has.

---

## GitHub Service Token

HubApi holds one `GitHub:ServiceToken` in config — a PAT or GitHub App token with access to the FlatPlanet-Hub org. This is used for:
- Adding/removing repo collaborators when members are added/removed
- Syncing `DATA_DICTIONARY.md` after DDL operations (Feature 4)
- Seeding initial files on project creation

Users **never** authenticate with GitHub through HubApi. They only need their `github_username` stored in the Security Platform user record.

```json
"GitHub": {
  "ServiceToken": "ghp_...",
  "OrgName": "FlatPlanet-Hub"
}
```
