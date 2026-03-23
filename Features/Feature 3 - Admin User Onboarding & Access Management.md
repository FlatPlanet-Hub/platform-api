# FlatPlanet.Platform — Feature 3: Admin User Onboarding & Access Management

## What This Is
Admin functionality to onboard users from accepted GitHub collaborators and manage their access. This feature provides the admin UI/API layer on top of Feature 6 (Flat Planet IAM).

**All underlying tables (users, roles, permissions, user_app_roles) live in Feature 6.** This feature defines the admin-specific endpoints and workflows for the current project.

## Architecture
```
Existing Accepted Collaborators View (already built)
    ↓ Admin clicks "Onboard"
Admin API endpoints (this feature)
    ↓
Feature 6 IAM tables (users, user_app_roles, roles, etc.)
```

## How Onboarding Works

### Pre-requisite
The current project must be registered as an `apps` entry in Feature 6. This happens once during initial setup:
```json
POST /api/apps
{
  "name": "Current Project",
  "slug": "current-project",
  "baseUrl": "https://your-app.com",
  "companyId": "flatplanet-company-uuid"
}
```

Roles and permissions for this app are also created once:
```json
POST /api/apps/{appId}/roles
{ "name": "owner", "description": "Full project access" }

POST /api/apps/{appId}/roles
{ "name": "developer", "description": "Read, write, and DDL access" }

POST /api/apps/{appId}/roles
{ "name": "viewer", "description": "Read-only access" }
```

```json
POST /api/apps/{appId}/permissions
{ "name": "read", "description": "Query and view data", "category": "data" }

POST /api/apps/{appId}/permissions
{ "name": "write", "description": "Insert, update, delete data", "category": "data" }

POST /api/apps/{appId}/permissions
{ "name": "ddl", "description": "Create and alter tables", "category": "schema" }

POST /api/apps/{appId}/permissions
{ "name": "manage_members", "description": "Invite and remove members", "category": "project" }
```

### Onboarding Flow
1. Admin sees accepted GitHub collaborators in the existing view
2. Admin clicks "Onboard" on a user
3. Admin inputs: first name, last name, email, selects role
4. Backend calls Feature 6 endpoints:
   - `POST /api/auth/register` or direct insert into `users` (with Supabase Auth user creation)
   - Links GitHub identity via `user_oauth_links`
   - Grants app access via `POST /api/apps/{appId}/users` → creates `user_app_roles` row
5. User can now log in via GitHub OAuth (Feature 2) and access the project

---

## API ENDPOINTS

All endpoints require admin access (app_admin role or manage_users permission, enforced by Feature 6).

### Users

- `GET /api/admin/users` — List all users created in the database
  
  Query params: `?search={name or email}&status={active|inactive|suspended}&roleId={uuid}&page=1&pageSize=20`
  
  Backend: queries Feature 6 `users` table joined with `user_app_roles` filtered to current app.
  
  Response:
  ```json
  {
    "success": true,
    "data": {
      "users": [
        {
          "id": "user-uuid",
          "email": "chris@example.com",
          "fullName": "Chris Moriarty",
          "roleTitle": "Senior Designer",
          "status": "active",
          "companyName": "Flat Planet Australia",
          "githubUsername": "Chris-Moriarty",
          "avatarUrl": "https://avatars.githubusercontent.com/u/266598407?v=4",
          "appRoles": [
            {
              "roleId": "role-uuid",
              "roleName": "developer",
              "permissions": ["read", "write", "ddl"],
              "grantedAt": "2025-03-19T...",
              "grantedBy": "admin-name"
            }
          ],
          "lastSeenAt": "2025-03-22T...",
          "createdAt": "2025-03-19T..."
        }
      ],
      "totalCount": 42,
      "page": 1,
      "pageSize": 20
    }
  }
  ```

- `GET /api/admin/users/{id}` — Get single user detail

- `POST /api/admin/users` — Onboard user from accepted GitHub collaborator
  ```json
  {
    "githubId": 266598407,
    "githubUsername": "Chris-Moriarty",
    "avatarUrl": "https://avatars.githubusercontent.com/u/266598407?v=4",
    "email": "chris@example.com",
    "fullName": "Chris Moriarty",
    "roleTitle": "Senior Designer",
    "companyId": "company-uuid",
    "roleId": "role-uuid"
  }
  ```
  Backend steps:
  1. Create Supabase Auth user (generates uid)
  2. Insert into Feature 6 `users` table (id = Supabase Auth uid)
  3. Insert into `user_oauth_links` (link GitHub identity)
  4. Insert into `user_app_roles` (grant access with selected role, `granted_by` = current admin)
  5. Log `user_created` + `role_granted` in `auth_audit_log`

- `POST /api/admin/users/bulk` — Onboard multiple users at once
  ```json
  {
    "users": [
      {
        "githubId": 266598407,
        "githubUsername": "Chris-Moriarty",
        "email": "chris@example.com",
        "fullName": "Chris Moriarty",
        "companyId": "company-uuid",
        "roleId": "role-uuid"
      }
    ]
  }
  ```

- `PUT /api/admin/users/{id}` — Update user details
  ```json
  {
    "fullName": "Christopher Moriarty",
    "email": "chris.new@example.com",
    "roleTitle": "Lead Designer"
  }
  ```
  Backend: updates Feature 6 `users` table.

- `PUT /api/admin/users/{id}/status` — Activate/deactivate/suspend user
  ```json
  { "status": "inactive" }
  ```
  Backend: updates `users.status`. If deactivating, also calls Feature 6 to revoke all sessions, tokens, and active grants. Logs `user_deactivated` in `auth_audit_log`.

- `PUT /api/admin/users/{id}/role` — Change user's role on this app
  ```json
  { "roleId": "new-role-uuid" }
  ```
  Backend: updates `user_app_roles` for this app. Logs `role_revoked` + `role_granted` in `auth_audit_log`.

### Roles (for this app)

All role CRUD goes through Feature 6 endpoints scoped to this app:

- `GET /api/admin/roles` — List roles for this app
  Backend: `GET /api/apps/{appId}/roles` (Feature 6)

- `POST /api/admin/roles` — Create role
  ```json
  {
    "name": "team_lead",
    "description": "Can read, write, and manage