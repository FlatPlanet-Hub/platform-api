# FlatPlanet.Platform — Feature 6: Flat Planet IAM — Centralized Identity & Access Management

## What This Is
A centralized authentication and authorization platform that any Flat Planet app can plug into. This replaces the auth/authorization defined in Features 2 and 3 with a proper multi-app IAM system based on the agreed Flat Planet Security Platform Schema v0.1.

This feature:
1. Implements all agreed tables from the schema doc
2. Finalizes the pending tables (policy, verification, audit)
3. Adds the missing tables needed for a complete IAM platform
4. Provides API endpoints that any app (including the current project) calls for auth/authorization
5. Follows ISO 27001 compliance requirements

## Architecture
```
Any Flat Planet App (Tala, current project, future apps)
    ↓
Flat Planet IAM API (.NET 8)
    ↓
Supabase Auth (handles password hashing, email verification, OAuth)
    +
Supabase Postgres (IAM schema — all tables below)
```

Supabase Auth handles the identity primitives (signup, login, password reset, OAuth). This API wraps Supabase Auth and adds the Flat Planet-specific layers: company assignment, app registration, role management, resource protection, policy enforcement, and audit logging.

## Tech Stack
- .NET 8 Web API
- Supabase Auth (identity provider)
- Supabase Postgres (IAM data)
- Npgsql + Dapper
- JWT Bearer authentication (Supabase-issued JWTs)

## Design Principles (from schema doc)
- Everything that might vary — varies through data, not through structure
- A resource can be a page, section, panel, or API endpoint — granularity defined by what is registered
- Roles are defined per app — the platform does not prescribe role names
- Policy parameters are flexible — new parameters without schema changes
- Employment relationship (company) is separate from access relationship (app roles)
- granted_by is always recorded
- No row in user_app_roles = no access. Default is always denial.

---

## DATABASE SCHEMA

### Layer 1: Registry (AGREED — from schema doc)

```sql
-- ─── COMPANIES ─────────────────────────────────────────
CREATE TABLE companies (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT NOT NULL,
    country_code TEXT NOT NULL,           -- ISO 3166-1 alpha-2
    status TEXT NOT NULL DEFAULT 'active', -- active / suspended
    created_at TIMESTAMPTZ DEFAULT now()
);

-- ─── APPS ──────────────────────────────────────────────
CREATE TABLE apps (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    company_id UUID NOT NULL REFERENCES companies(id),
    name TEXT NOT NULL,
    slug TEXT UNIQUE NOT NULL,
    base_url TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'active', -- active / inactive / suspended
    registered_at TIMESTAMPTZ DEFAULT now(),
    registered_by UUID NOT NULL           -- FK → users (deferred, users may not exist yet at migration time)
);

-- ─── RESOURCE TYPES ────────────────────────────────────
CREATE TABLE resource_types (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT UNIQUE NOT NULL,
    description TEXT
);

INSERT INTO resource_types (name, description) VALUES
    ('page', 'A full HTML page or route'),
    ('section', 'A named section within a page'),
    ('panel', 'A UI panel — may be visible to some roles and hidden from others'),
    ('api_endpoint', 'A serverless function or API route');

-- ─── RESOURCES ─────────────────────────────────────────
CREATE TABLE resources (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_id UUID NOT NULL REFERENCES apps(id),
    resource_type_id UUID NOT NULL REFERENCES resource_types(id),
    name TEXT NOT NULL,
    identifier TEXT NOT NULL,             -- path or selector e.g. /admin, #qc-panel
    status TEXT NOT NULL DEFAULT 'active', -- active / inactive
    created_at TIMESTAMPTZ DEFAULT now()
);
```

### Layer 2: Identity + Access (AGREED — from schema doc)

```sql
-- ─── USERS ─────────────────────────────────────────────
CREATE TABLE users (
    id UUID PRIMARY KEY,                  -- matches Supabase Auth uid
    company_id UUID NOT NULL REFERENCES companies(id),
    email TEXT UNIQUE NOT NULL,
    full_name TEXT NOT NULL,
    role_title TEXT,                       -- job title, not platform role
    status TEXT NOT NULL DEFAULT 'active', -- active / inactive / suspended
    created_at TIMESTAMPTZ DEFAULT now(),
    last_seen_at TIMESTAMPTZ
);

-- ─── ROLES ─────────────────────────────────────────────
CREATE TABLE roles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_id UUID REFERENCES apps(id),      -- null for platform-level roles
    name TEXT NOT NULL,
    description TEXT,
    is_platform_role BOOLEAN DEFAULT false,
    created_at TIMESTAMPTZ DEFAULT now(),
    UNIQUE(app_id, name)
);

-- Seed platform-level roles
INSERT INTO roles (name, description, is_platform_role) VALUES
    ('platform_owner', 'Full platform access across all apps and companies', true),
    ('app_admin', 'Admin access within a specific app', true);

-- ─── USER APP ROLES ────────────────────────────────────
CREATE TABLE user_app_roles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    app_id UUID NOT NULL REFERENCES apps(id),
    role_id UUID NOT NULL REFERENCES roles(id),
    granted_at TIMESTAMPTZ DEFAULT now(),
    granted_by UUID NOT NULL REFERENCES users(id),
    expires_at TIMESTAMPTZ,               -- nullable, for temporary access
    status TEXT NOT NULL DEFAULT 'active', -- active / suspended / expired
    UNIQUE(user_id, app_id, role_id)
);
```

### Layer 3: Permissions (NEW — needed for granular access control)

```sql
-- ─── PERMISSIONS ───────────────────────────────────────
-- Granular action-level permissions. Defined per app.
-- Follows the same principle: permissions are data, not code.
CREATE TABLE permissions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_id UUID REFERENCES apps(id),      -- null for platform-level permissions
    name TEXT NOT NULL,
    description TEXT,
    category TEXT NOT NULL,               -- e.g. 'data', 'schema', 'admin', 'ui'
    created_at TIMESTAMPTZ DEFAULT now(),
    UNIQUE(app_id, name)
);

-- Seed platform-level permissions
INSERT INTO permissions (name, description, category) VALUES
    ('manage_companies', 'Create and edit company records', 'admin'),
    ('manage_users', 'Create, edit, and deactivate users', 'admin'),
    ('manage_apps', 'Register and configure apps', 'admin'),
    ('manage_roles', 'Create and edit roles and permissions', 'admin'),
    ('manage_policies', 'Create and edit resource policies', 'admin'),
    ('view_audit_log', 'View auth and access audit logs', 'admin'),
    ('manage_resources', 'Register and configure protected resources', 'admin');

-- ─── ROLE PERMISSIONS ──────────────────────────────────
-- Maps roles to permissions. A role can have many permissions.
CREATE TABLE role_permissions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    role_id UUID NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    permission_id UUID NOT NULL REFERENCES permissions(id) ON DELETE CASCADE,
    granted_at TIMESTAMPTZ DEFAULT now(),
    granted_by UUID REFERENCES users(id),
    UNIQUE(role_id, permission_id)
);
```

### Layer 4: Policy (PENDING — finalizing from schema doc)

```sql
-- ─── RESOURCE POLICIES ─────────────────────────────────
-- Flexible key-value policy parameters per resource.
-- Examples: idle_timeout_minutes=15, require_2fa=true, allowed_hours=09:00-18:00
CREATE TABLE resource_policies (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    resource_id UUID NOT NULL REFERENCES resources(id) ON DELETE CASCADE,
    policy_key TEXT NOT NULL,
    policy_value TEXT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT now(),
    updated_at TIMESTAMPTZ DEFAULT now(),
    created_by UUID REFERENCES users(id),
    UNIQUE(resource_id, policy_key)
);

-- Seed known policy keys as reference (not enforced by FK — new keys can be added freely)
-- idle_timeout_minutes    — minutes of inactivity before session lock
-- absolute_timeout_minutes — max session duration regardless of activity
-- require_2fa             — true/false
-- allowed_days            — e.g. mon,tue,wed,thu,fri
-- allowed_hours_start     — e.g. 09:00
-- allowed_hours_end       — e.g. 18:00
-- max_concurrent_sessions — e.g. 3
-- ip_whitelist            — comma-separated CIDRs

-- ─── PLATFORM CONFIG ───────────────────────────────────
-- Global platform settings. Key-value. Extensible.
CREATE TABLE platform_config (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    config_key TEXT UNIQUE NOT NULL,
    config_value TEXT NOT NULL,
    description TEXT,
    updated_at TIMESTAMPTZ DEFAULT now(),
    updated_by UUID REFERENCES users(id)
);

INSERT INTO platform_config (config_key, config_value, description) VALUES
    ('default_idle_timeout_minutes', '30', 'Default session idle timeout if not set per resource'),
    ('default_absolute_timeout_minutes', '480', 'Default max session duration (8 hours)'),
    ('default_require_2fa', 'false', 'Whether 2FA is required by default'),
    ('jwt_access_expiry_minutes', '60', 'JWT access token expiry'),
    ('jwt_refresh_expiry_days', '7', 'JWT refresh token expiry'),
    ('claude_token_expiry_days', '30', 'Claude/service token expiry'),
    ('max_failed_login_attempts', '5', 'Lock account after N failed attempts'),
    ('lockout_duration_minutes', '30', 'Account lockout duration');
```

### Layer 5: Sessions + Tokens (NEW — needed for session management and service auth)

```sql
-- ─── SESSIONS ──────────────────────────────────────────
-- Active session tracking. Enforces idle timeout, absolute timeout, max concurrent.
CREATE TABLE sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    app_id UUID REFERENCES apps(id),      -- null for platform-level sessions
    ip_address TEXT,
    user_agent TEXT,
    started_at TIMESTAMPTZ DEFAULT now(),
    last_active_at TIMESTAMPTZ DEFAULT now(),
    expires_at TIMESTAMPTZ NOT NULL,
    is_active BOOLEAN DEFAULT true,
    ended_reason TEXT                      -- logout / idle_timeout / absolute_timeout / revoked / superseded
);

CREATE INDEX idx_sessions_user ON sessions(user_id);
CREATE INDEX idx_sessions_active ON sessions(is_active) WHERE is_active = true;

-- ─── REFRESH TOKENS ────────────────────────────────────
CREATE TABLE refresh_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    session_id UUID REFERENCES sessions(id) ON DELETE CASCADE,
    token_hash TEXT UNIQUE NOT NULL,       -- SHA256 hash, never plaintext
    expires_at TIMESTAMPTZ NOT NULL,
    revoked BOOLEAN DEFAULT false,
    revoked_at TIMESTAMPTZ,
    revoked_reason TEXT,                   -- rotation / logout / admin_revoke / password_change
    created_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_refresh_tokens_user ON refresh_tokens(user_id);

-- ─── API TOKENS ────────────────────────────────────────
-- Long-lived tokens for service-to-service auth (Claude, CI/CD, integrations).
-- Scoped to a specific app or platform-wide.
CREATE TABLE api_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id),     -- who generated it
    app_id UUID REFERENCES apps(id),                 -- null = platform-wide
    name TEXT NOT NULL,                               -- human label e.g. "Claude token for Tala"
    token_hash TEXT UNIQUE NOT NULL,                  -- SHA256 hash
    permissions TEXT[] NOT NULL,                       -- scoped permissions e.g. {"read", "write"}
    expires_at TIMESTAMPTZ NOT NULL,
    revoked BOOLEAN DEFAULT false,
    revoked_at TIMESTAMPTZ,
    last_used_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_api_tokens_user ON api_tokens(user_id);
```

### Layer 6: OAuth + MFA (NEW — needed for social login and 2FA)

```sql
-- ─── OAUTH PROVIDERS ───────────────────────────────────
-- Tracks which external identity providers are configured.
CREATE TABLE oauth_providers (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT UNIQUE NOT NULL,            -- e.g. 'github', 'google'
    client_id TEXT NOT NULL,
    is_enabled BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT now()
);
-- Note: client_secret stored in appsettings.json or vault, NOT in DB

-- ─── USER OAUTH LINKS ─────────────────────────────────
-- Links a platform user to one or more OAuth identities.
CREATE TABLE user_oauth_links (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    provider_id UUID NOT NULL REFERENCES oauth_providers(id),
    provider_user_id TEXT NOT NULL,       -- e.g. GitHub user ID
    provider_username TEXT,               -- e.g. GitHub login
    provider_email TEXT,
    provider_avatar_url TEXT,
    access_token_encrypted TEXT,          -- AES-256 encrypted, for API operations
    linked_at TIMESTAMPTZ DEFAULT now(),
    UNIQUE(provider_id, provider_user_id),
    UNIQUE(user_id, provider_id)
);

-- ─── USER MFA ──────────────────────────────────────────
-- 2FA enrolment records. Supports TOTP and future methods.
CREATE TABLE user_mfa (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    method TEXT NOT NULL,                 -- 'totp', 'sms', 'email'
    secret_encrypted TEXT,                -- AES-256 encrypted TOTP secret
    phone_number TEXT,                    -- for SMS method
    is_enabled BOOLEAN DEFAULT false,
    is_verified BOOLEAN DEFAULT false,    -- user has completed setup
    backup_codes_hash TEXT[],             -- hashed backup codes
    enrolled_at TIMESTAMPTZ DEFAULT now(),
    verified_at TIMESTAMPTZ,
    UNIQUE(user_id, method)
);
```

### Layer 7: Verification (PENDING — finalizing from schema doc)

```sql
-- ─── VERIFICATION EVENTS ───────────────────────────────
-- Records of identity verification calls.
CREATE TABLE verification_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    verified_user_id UUID NOT NULL REFERENCES users(id),
    verified_by_user_id UUID NOT NULL REFERENCES users(id),
    method TEXT NOT NULL,                 -- 'video_call', 'in_person', 'document_check', 'manager_vouch'
    outcome TEXT NOT NULL,                -- 'verified', 'failed', 'inconclusive'
    recording_reference TEXT,             -- link to recording if applicable
    notes TEXT,
    verified_at TIMESTAMPTZ DEFAULT now()
);
```

### Layer 8: Audit (PENDING — finalizing from schema doc)

```sql
-- ─── AUTH AUDIT LOG ────────────────────────────────────
-- Immutable append-only log. Every auth event recorded.
CREATE TABLE auth_audit_log (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES users(id),
    app_id UUID REFERENCES apps(id),
    event_type TEXT NOT NULL,
    ip_address TEXT,
    user_agent TEXT,
    details JSONB,                        -- flexible extra context
    created_at TIMESTAMPTZ DEFAULT now()
);

-- Event types:
-- login_success, login_failure, logout, token_refresh, token_revoke,
-- mfa_enrol, mfa_verify, mfa_failure,
-- session_start, session_end, session_idle_timeout, session_absolute_timeout,
-- password_change, password_reset_request, password_reset_complete,
-- account_locked, account_unlocked,
-- role_granted, role_revoked, role_expired,
-- api_token_created, api_token_revoked,
-- oauth_link, oauth_unlink,
-- user_created, user_deactivated, user_reactivated

CREATE INDEX idx_auth_audit_user ON auth_audit_log(user_id);
CREATE INDEX idx_auth_audit_app ON auth_audit_log(app_id);
CREATE INDEX idx_auth_audit_type ON auth_audit_log(event_type);
CREATE INDEX idx_auth_audit_created ON auth_audit_log(created_at);

-- Retention policy: partition by month, drop partitions older than configured retention period
-- Implementation: use pg_partman or manual PARTITION BY RANGE(created_at)

-- ─── ATTENDANCE EVENTS ─────────────────────────────────
-- Staff login events for future payroll use.
CREATE TABLE attendance_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id),
    event_type TEXT NOT NULL,             -- 'clock_in', 'clock_out', 'break_start', 'break_end'
    timestamp TIMESTAMPTZ DEFAULT now(),
    date_sydney DATE NOT NULL,            -- date in Sydney timezone for payroll grouping
    ip_address TEXT,
    notes TEXT
);

CREATE INDEX idx_attendance_user ON attendance_events(user_id);
CREATE INDEX idx_attendance_date ON attendance_events(date_sydney);
```

### Layer 9: Data Protection + Compliance (NEW — ISO 27001)

```sql
-- ─── DATA CLASSIFICATION ───────────────────────────────
-- Sensitivity tiers per resource. Required for ISO 27001.
CREATE TABLE data_classification (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    resource_id UUID NOT NULL REFERENCES resources(id) ON DELETE CASCADE,
    classification TEXT NOT NULL,          -- 'public', 'internal', 'confidential', 'restricted'
    handling_notes TEXT,                   -- how data in this resource should be handled
    classified_by UUID REFERENCES users(id),
    classified_at TIMESTAMPTZ DEFAULT now(),
    UNIQUE(resource_id)
);

-- ─── CONSENT RECORDS ───────────────────────────────────
-- User consent tracking for GDPR / privacy compliance.
CREATE TABLE consent_records (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    consent_type TEXT NOT NULL,            -- 'terms_of_service', 'privacy_policy', 'data_processing', 'marketing'
    version TEXT NOT NULL,                 -- version of the document consented to
    consented BOOLEAN NOT NULL,
    ip_address TEXT,
    consented_at TIMESTAMPTZ DEFAULT now(),
    withdrawn_at TIMESTAMPTZ
);

CREATE INDEX idx_consent_user ON consent_records(user_id);

-- ─── INCIDENT LOG ──────────────────────────────────────
-- Security incident tracking. Required for ISO 27001 incident response.
CREATE TABLE incident_log (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    reported_by UUID REFERENCES users(id),
    severity TEXT NOT NULL,               -- 'low', 'medium', 'high', 'critical'
    title TEXT NOT NULL,
    description TEXT NOT NULL,
    affected_app_id UUID REFERENCES apps(id),
    affected_users_count INTEGER,
    status TEXT NOT NULL DEFAULT 'open',   -- 'open', 'investigating', 'resolved', 'closed'
    resolution TEXT,
    reported_at TIMESTAMPTZ DEFAULT now(),
    resolved_at TIMESTAMPTZ,
    closed_at TIMESTAMPTZ
);
```

---

## API ENDPOINTS

### Authentication (wraps Supabase Auth)
- `POST /api/auth/register` — Register user (creates Supabase Auth user + platform users record)
  ```json
  {
    "email": "user@example.com",
    "password": "securepassword",
    "fullName": "Chris Moriarty",
    "companyId": "company-uuid",
    "roleTitle": "Senior Designer"
  }
  ```
- `POST /api/auth/login` — Login via Supabase Auth, create session, return JWT + refresh token
- `POST /api/auth/logout` — End session, revoke refresh token
- `POST /api/auth/refresh` — Rotate refresh token, extend session
- `GET /api/auth/me` — Current user profile + roles + apps + permissions

### OAuth
- `GET /api/auth/oauth/{provider}` — Redirect to OAuth consent screen (GitHub, Google)
- `GET /api/auth/oauth/{provider}/callback` — Handle callback, link/create user
- `GET /api/auth/oauth/links` — List user's linked OAuth providers
- `DELETE /api/auth/oauth/links/{providerId}` — Unlink OAuth provider

### MFA
- `POST /api/auth/mfa/enrol` — Start 2FA enrolment (returns QR code for TOTP)
  ```json
  { "method": "totp" }
  ```
- `POST /api/auth/mfa/verify` — Verify 2FA code to complete enrolment
- `POST /api/auth/mfa/challenge` — Submit 2FA code during login
- `DELETE /api/auth/mfa` — Disable 2FA (requires re-authentication)
- `GET /api/auth/mfa/backup-codes` — Generate new backup codes

### Sessions
- `GET /api/auth/sessions` — List active sessions for current user
- `DELETE /api/auth/sessions/{sessionId}` — Revoke a specific session
- `DELETE /api/auth/sessions` — Revoke all sessions (force logout everywhere)

### API Tokens (for Claude, CI/CD, integrations)
- `POST /api/auth/api-tokens` — Generate scoped API token
  ```json
  {
    "name": "Claude token for Tala",
    "appId": "app-uuid",
    "permissions": ["read", "write"],
    "expiryDays": 30
  }
  ```
- `GET /api/auth/api-tokens` — List active tokens for current user
- `DELETE /api/auth/api-tokens/{tokenId}` — Revoke token

### Companies (platform_owner only)
- `POST /api/companies` — Create company
- `GET /api/companies` — List companies
- `GET /api/companies/{id}` — Get company detail
- `PUT /api/companies/{id}` — Update company
- `PUT /api/companies/{id}/status` — Suspend/activate company

### Apps (platform_owner or app_admin)
- `POST /api/apps` — Register new app
  ```json
  {
    "name": "Tala",
    "slug": "tala",
    "baseUrl": "https://tala-ondemand.netlify.app",
    "companyId": "company-uuid"
  }
  ```
- `GET /api/apps` — List apps (filtered by user's access)
- `GET /api/apps/{id}` — Get app detail
- `PUT /api/apps/{id}` — Update app
- `PUT /api/apps/{id}/status` — Suspend/activate app

### Resources (app_admin)
- `POST /api/apps/{appId}/resources` — Register protected resource
  ```json
  {
    "resourceTypeId": "type-uuid",
    "name": "Admin Panel",
    "identifier": "/admin"
  }
  ```
- `GET /api/apps/{appId}/resources` — List app's resources
- `PUT /api/apps/{appId}/resources/{id}` — Update resource
- `DELETE /api/apps/{appId}/resources/{id}` — Deactivate resource

### Resource Types
- `GET /api/resource-types` — List all resource types
- `POST /api/resource-types` — Add new resource type (platform_owner only)

### Roles (app_admin for app roles, platform_owner for platform roles)
- `POST /api/apps/{appId}/roles` — Create role for an app
  ```json
  {
    "name": "manila_coordinator",
    "description": "Manages Manila office operations"
  }
  ```
- `GET /api/apps/{appId}/roles` — List app's roles
- `PUT /api/apps/{appId}/roles/{id}` — Update role
- `DELETE /api/apps/{appId}/roles/{id}` — Delete role (only if no users assigned)

### Permissions (app_admin)
- `POST /api/apps/{appId}/permissions` — Create permission
  ```json
  {
    "name": "approve_timesheets",
    "description": "Can approve staff timesheets",
    "category": "workflow"
  }
  ```
- `GET /api/apps/{appId}/permissions` — List app's permissions
- `PUT /api/apps/{appId}/permissions/{id}` — Update permission
- `POST /api/apps/{appId}/roles/{roleId}/permissions` — Assign permissions to role
  ```json
  {
    "permissionIds": ["perm-uuid-1", "perm-uuid-2"]
  }
  ```
- `DELETE /api/apps/{appId}/roles/{roleId}/permissions/{permId}` — Remove permission from role

### User Access Management (app_admin or manage_users permission)
- `POST /api/apps/{appId}/users` — Grant user access to app with role
  ```json
  {
    "userId": "user-uuid",
    "roleId": "role-uuid",
    "expiresAt": "2025-06-01T00:00:00Z"
  }
  ```
- `GET /api/apps/{appId}/users` — List users with access to app
- `PUT /api/apps/{appId}/users/{userId}/role` — Change user's role
- `DELETE /api/apps/{appId}/users/{userId}` — Revoke access
- `GET /api/users` — List all platform users (with search, filter, pagination)
- `GET /api/users/{id}` — Get user detail + all app access
- `PUT /api/users/{id}` — Update user details
- `PUT /api/users/{id}/status` — Activate/deactivate/suspend user

### Policies (app_admin or manage_policies)
- `POST /api/apps/{appId}/resources/{resourceId}/policies` — Set policy
  ```json
  {
    "policyKey": "idle_timeout_minutes",
    "policyValue": "15"
  }
  ```
- `GET /api/apps/{appId}/resources/{resourceId}/policies` — Get resource policies
- `PUT /api/apps/{appId}/resources/{resourceId}/policies/{key}` — Update policy value
- `DELETE /api/apps/{appId}/resources/{resourceId}/policies/{key}` — Remove policy

### Platform Config (platform_owner only)
- `GET /api/platform/config` — List all config values
- `PUT /api/platform/config/{key}` — Update config value

### Authorization Check (called by apps at runtime)
- `POST /api/authorize` — Check if a user can access a resource
  ```json
  {
    "userId": "user-uuid",
    "appSlug": "tala",
    "resourceIdentifier": "/admin",
    "requiredPermission": "approve_timesheets"
  }
  ```
  Response:
  ```json
  {
    "allowed": true,
    "roles": ["manila_coordinator"],
    "permissions": ["approve_timesheets", "view_reports"],
    "policies": {
      "idle_timeout_minutes": "15",
      "require_2fa": "true"
    }
  }
  ```
  This is the core endpoint every app calls to check access.

### Verification (manage_users permission)
- `POST /api/users/{id}/verify` — Record verification event
  ```json
  {
    "method": "video_call",
    "outcome": "verified",
    "recordingReference": "https://...",
    "notes": "Identity confirmed via video call"
  }
  ```
- `GET /api/users/{id}/verifications` — List verification events for user

### Audit Log (view_audit_log permission)
- `GET /api/audit/auth` — Query auth audit log
  Query params: `?userId={uuid}&appId={uuid}&eventType={type}&from={date}&to={date}&page=1&pageSize=50`
- `GET /api/audit/attendance` — Query attendance events
  Query params: `?userId={uuid}&dateFrom={date}&dateTo={date}`

### Compliance (platform_owner)
- `POST /api/apps/{appId}/resources/{resourceId}/classification` — Set data classification
  ```json
  {
    "classification": "confidential",
    "handlingNotes": "Contains PII — encrypt at rest, log all access"
  }
  ```
- `GET /api/compliance/consent/{userId}` — Get user's consent records
- `POST /api/compliance/consent` — Record user consent
  ```json
  {
    "consentType": "privacy_policy",
    "version": "1.2",
    "consented": true
  }
  ```
- `POST /api/compliance/incidents` — Report security incident
- `GET /api/compliance/incidents` — List incidents
- `PUT /api/compliance/incidents/{id}` — Update incident status/resolution

---

## AUTHORIZATION MODEL

### How apps check access at runtime
Every app calls `POST /api/authorize` before granting access. The IAM API:
1. Looks up the user's `user_app_roles` for the app
2. Checks role status (active, not expired)
3. Resolves `role_permissions` for the user's role
4. Checks `resource_policies` for the specific resource (2FA, allowed hours, etc.)
5. Returns allowed/denied + the user's permissions + applicable policies
6. Logs the check in `auth_audit_log`

### Default deny
No row in `user_app_roles` for a given app = no access. The API never returns `allowed: true` without an explicit grant.

### Policy enforcement
Policies are checked at authorization time. If a resource requires 2FA and the user hasn't completed it for this session, the response includes `"mfaRequired": true` so the app can prompt for it.

---

## HOW THIS REPLACES FEATURES 2 AND 3

| Feature 2/3 concept | Flat Planet IAM equivalent |
|---|---|
| `platform.users` | `users` table (tied to Supabase Auth) |
| `platform.roles` | `roles` table (per-app, flexible) |
| `platform.project_roles` | `roles` where `app_id` = your project's app ID |
| `platform.user_roles` + `platform.project_members` | `user_app_roles` (unified) |
| `platform.projects` | Registers as an `apps` entry |
| `platform.permissions` | `permissions` + `role_permissions` |
| `platform.custom_roles` | Just more rows in `roles` for the app |
| `platform.audit_log` | `auth_audit_log` (more comprehensive) |
| `platform.refresh_tokens` | `refresh_tokens` table |
| `platform.claude_tokens` | `api_tokens` table (generalized for any service) |
| GitHub OAuth login | `oauth_providers` + `user_oauth_links` |
| Admin user onboarding | `POST /api/apps/{appId}/users` with role assignment |

Features 2 and 3 should now reference Feature 6 for all auth/authorization instead of defining their own tables.

---

## SECURITY REQUIREMENTS (ISO 27001 ALIGNED)

### A.5/A.9 Access Control
1. Default deny — no access without explicit grant
2. RBAC with granular permissions per app
3. Temporary access with expiry dates
4. All grants tracked with `granted_by`
5. Periodic access review — endpoint to list all active grants with age

### A.10 Cryptography
6. OAuth tokens encrypted at rest (AES-256)
7. MFA secrets encrypted at rest (AES-256)
8. Refresh tokens and API tokens stored as SHA256 hashes only
9. TLS required for all API communication (HTTPS only)
10. Key rotation policy: JWT secret rotated every 90 days, AES key rotated annually

### A.12 Operations Security
11. Immutable append-only audit log
12. Rate limiting per user per endpoint
13. Account lockout after configurable failed attempts
14. Log retention: configurable via `platform_config`, default 365 days
15. Monitoring: failed login spikes, unusual access patterns, after-hours access

### A.13 Communications Security
16. CORS: strict allowed origins per app
17. Security headers: CSP, HSTS, X-Frame-Options, X-Content-Type-Options
18. API versioning for backward compatibility

### A.14 System Security
19. Input validation on all endpoints
20. Parameterized queries (Dapper)
21. Dependency scanning in CI/CD pipeline

### A.18 Compliance
22. Data classification per resource
23. User consent tracking (GDPR)
24. Incident response logging
25. Data retention policies configurable per table
26. User data export endpoint for GDPR right-of-access
27. User data deletion endpoint for GDPR right-to-erasure

### A.7 Human Resources
28. User deactivation cascades: revoke all sessions, tokens, and active grants
29. Offboarding: removes all `user_app_roles`, revokes tokens, unlinks OAuth, logs event

---

## appsettings.json
```json
{
  "Supabase": {
    "Url": "https://your-project.supabase.co",
    "ServiceRoleKey": "YOUR_SERVICE_ROLE_KEY",
    "JwtSecret": "YOUR_SUPABASE_JWT_SECRET",
    "DbHost": "aws-0-us-east-1.pooler.supabase.com",
    "DbPort": 6543,
    "DbName": "postgres",
    "DbUser": "postgres.YOUR_PROJECT_REF",
    "DbPassword": "YOUR_DB_PASSWORD"
  },
  "OAuth": {
    "GitHub": {
      "ClientId": "YOUR_GITHUB_CLIENT_ID",
      "ClientSecret": "YOUR_GITHUB_CLIENT_SECRET",
      "RedirectUri": "https://your-api.com/api/auth/oauth/github/callback"
    },
    "Google": {
      "ClientId": "YOUR_GOOGLE_CLIENT_ID",
      "ClientSecret": "YOUR_GOOGLE_CLIENT_SECRET",
      "RedirectUri": "https://your-api.com/api/auth/oauth/google/callback"
    }
  },
  "Encryption": {
    "Key": "YOUR_AES_256_KEY"
  },
  "Cors": {
    "AllowedOrigins": ["https://tala-ondemand.netlify.app", "https://your-app.com"]
  }
}
```

---

## RESPONSE FORMAT
```json
{
  "success": true,
  "data": { ... },
  "error": null
}
```
