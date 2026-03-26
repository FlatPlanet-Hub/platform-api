# FlatPlanet Hub — Feature 6: Security Platform Integration

## What This Was
Feature 6 was the original IAM layer built inside HubApi — companies, users, apps, roles, permissions, sessions, audit log, OAuth, MFA, compliance.

## What Happened
This entire layer was extracted into a standalone service: **flatplanet-security-platform**.

The Security Platform is a separate API that all FlatPlanet apps plug into for authentication and authorization — equivalent to Microsoft Entra for FlatPlanet's ecosystem.

---

## What Was Removed From HubApi

The following tables, services, repositories, and controllers no longer exist in HubApi:

| Removed | Owned by Security Platform |
|---------|---------------------------|
| `companies` table | ✓ |
| `users` table (platform identity) | ✓ |
| `apps` table | ✓ |
| `roles`, `permissions`, `role_permissions` | ✓ |
| `user_app_roles` | ✓ |
| `resource_types`, `resources` | ✓ |
| `sessions`, `refresh_tokens` | ✓ |
| `auth_audit_log` | ✓ |
| `oauth_providers`, `user_oauth_links` | ✓ |
| `user_mfa` | ✓ |
| `login_attempts`, `security_config` | ✓ |
| `AdminUserController` | ✓ |
| `AdminRoleController` | ✓ |
| `AdminPermissionController` | ✓ |
| `CompaniesController` | ✓ |
| `AppsController` | ✓ |
| `AuthorizeController` | ✓ |
| `AuditController` | ✓ |
| `ComplianceController` | ✓ |
| `IGitHubOAuthService` | ✓ |
| `IJwtService` (app token issuance) | ✓ |
| GitHub OAuth flow | ✓ |

---

## How HubApi Integrates With the Security Platform

### 1. JWT Validation
HubApi configures `JwtBearer` using the same secret and issuer as the Security Platform. All Security Platform JWTs are automatically validated by ASP.NET Core middleware — no extra code.

### 2. Project Registration → Security Platform App
When a project is created in HubApi (`POST /api/projects`), HubApi calls the Security Platform to register it as an app and grant the creator the `owner` role:

```
POST {SecurityPlatform}/api/v1/apps
{ name, slug, baseUrl, companyId }

POST {SecurityPlatform}/api/v1/apps/{appId}/users
{ userId, roleId }  ← owner role
```

This is a server-to-server call using a platform service token configured in HubApi's appsettings.

### 3. Project Visibility
When a user calls `GET /api/projects`, HubApi calls Security Platform to get the user's active app roles:

```
GET {SecurityPlatform}/api/v1/user-context?appSlug={slug}
```

Only projects where the user has an active role are returned.

### 4. Permission Checks
Before sensitive operations (add member, delete project), HubApi calls:

```
POST {SecurityPlatform}/api/v1/authorize
{
  "appSlug": "project-slug",
  "resourceIdentifier": "project/members",
  "requiredPermission": "manage_members"
}
```

### 5. Member Management → Security Platform
When HubApi adds or removes a project member (Feature 3), it calls Security Platform to grant or revoke the `user_app_roles` entry.

---

## Configuration

```json
"SecurityPlatform": {
  "BaseUrl": "https://security.flatplanet.com",
  "ServiceToken": "platform-to-platform-service-token"
},
"Jwt": {
  "Issuer": "flatplanet-security",
  "Audience": "flatplanet-apps",
  "SecretKey": "MUST_MATCH_SECURITY_PLATFORM_SECRET"
}
```

---

## What HubApi Still Owns

HubApi owns only what is specific to the Hub's functionality:

| HubApi Table | Purpose |
|---|---|
| `projects` | Project registry — name, description, schema name, GitHub repo, owner, status |
| `api_tokens` | Claude Code API tokens — scoped to a project schema, issued for CLAUDE.md |

Everything else (identity, roles, permissions, audit, sessions) lives in the Security Platform.
