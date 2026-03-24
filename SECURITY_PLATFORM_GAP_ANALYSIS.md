# Security Platform Gap Analysis
## Schema v0.1 (2026-03-14) vs Implemented Code

**Branch:** `feature/github-repo-operations`
**Analysis date:** 2026-03-24
**Reference document:** `Flat-Planet-Security-Platform-Schema-v0.1.docx`

---

## What the Document Defines

The schema document (v0.1) is divided into two explicit sections:

### Agreed — 7 tables (Session 2)

| Table | Purpose |
|---|---|
| `companies` | Top-level entity. Employing entity for users, owning entity for apps. |
| `users` | Platform identities linked to Supabase Auth. Belong to one company. |
| `apps` | Registered applications. Each app defines its own resources and roles. |
| `resource_types` | Lookup table for protectable resource granularity (page, section, panel, api_endpoint). |
| `resources` | Protected entities within apps. Registered by path or selector. |
| `roles` | App-defined role definitions. Platform roles flagged with `is_platform_role`. |
| `user_app_roles` | Access assignment table. No row = no access. `granted_by` always recorded. |

### Pending — 5 groups (not yet agreed, agenda for next session)

| Group | Description |
|---|---|
| Policy layer | Resource-level session config — idle timeout, absolute max, 2FA requirements, allowed hours. Flexible key-value design. |
| Verification events | Records of identity verification calls — who verified, method, recording reference, outcome. |
| Auth audit log | Every auth event — login, logout, failure, MFA, session expiry, password reset. Immutable, append-only. |
| Attendance events | Staff login events for payroll. Timestamp in Sydney timezone. |
| Platform config | Global platform settings. Key-value, extensible without schema changes. |

---

## What the Code Built

### Agreed tables (all 7) — present in DB and API ✅

| Table | Migration | Controller |
|---|---|---|
| `companies` | 003 | `CompaniesController` |
| `users` | 001 + 003 | `AdminUserController` / `AuthController` |
| `apps` | 003 | `AppsController` |
| `resource_types` | 003 | `ResourceTypesController` |
| `resources` | 003 | `ResourcesController` |
| `roles` | 001 + 003 | `RoleController` |
| `user_app_roles` | 003 | `AppsController` (grant/revoke/change-role) |

### Pending tables — all 5 groups built before agreement ⚠️

| Pending Group | Migration | API Coverage |
|---|---|---|
| Policy layer | 003 — `resource_policies`, `platform_config` | None |
| Verification events | 004 — `verification_events` | None |
| Auth audit log | 004 — `auth_audit_log` | `AuditController` (query only) |
| Attendance events | 004 — `attendance_events` | None |
| Platform config | 003 — `platform_config` | None |

### Built beyond document scope entirely

These tables have no equivalent in the document — they were built on assumed design decisions:

| Table | Layer | Notes |
|---|---|---|
| `user_mfa` | MFA | TOTP, SMS, email methods. Backup codes. |
| `data_classification` | Data protection | public / internal / confidential / restricted per resource. |
| `consent_records` | Compliance | Terms, privacy, data processing, marketing consent. |
| `incident_log` | Compliance | Severity-tiered incident management. |
| `permissions` + `role_permissions` | RBAC | Full permission sub-layer on top of roles. |
| `oauth_providers` + `user_oauth_links` | OAuth | GitHub OAuth infrastructure. |
| `sessions` + `refresh_tokens` + `api_tokens` | Token management | Custom auth stack, not Supabase Auth. |

---

## Gaps and Issues

### 1. `users.last_seen_at` — in domain entity, never migrated

**Severity: High — runtime failure**

`User.cs:13` declares `LastSeenAt`. None of the 4 migration files contain:
```sql
ALTER TABLE platform.users ADD COLUMN last_seen_at TIMESTAMPTZ;
```
The document requires this column: *"Updated on every successful auth."* Any code path that writes `last_seen_at` will silently fail or throw at the database layer.

**Fix required:**
```sql
ALTER TABLE platform.users ADD COLUMN IF NOT EXISTS last_seen_at TIMESTAMPTZ;
```

---

### 2. `users.github_id NOT NULL` — blocks the document's user model

**Severity: High — blocks spec-compliant user creation**

Migration 001 defined:
```sql
github_id BIGINT UNIQUE NOT NULL
```
Migration 003 adds `company_id`, `full_name`, `role_title` etc. but never relaxes this constraint.

The document defines `users` as platform identities belonging to a company. There is no mention of GitHub in the users table. The document's admin onboarding flow (create a user, assign to a company, grant app access) is impossible at the DB level without a GitHub ID.

**Fix required:**
```sql
ALTER TABLE platform.users ALTER COLUMN github_id DROP NOT NULL;
```

---

### 3. `apps.registered_at` named `created_at` in the schema

**Severity: Low — naming inconsistency**

Document spec column: `registered_at`
DB schema: `created_at`

The column tracks when the app was registered, not generically "created." The name matters for API consumers who read the spec.

---

### 4. No attendance events API

**Severity: Medium — table with no interface**

`attendance_events` exists in migration 004. No controller, service, or repository exposes it. The document describes this as recording staff login events for payroll with date in Sydney timezone. Without an API, no system can write to it and no payroll integration can read from it.

**Missing:**
- `IAttendanceService` + implementation
- `AttendanceController` (`POST /api/attendance`, `GET /api/attendance?userId=&from=&to=`)
- Repository

---

### 5. No verification events API

**Severity: Medium — table with no interface**

`verification_events` exists in migration 004. No controller, service, or repository exposes it. The document describes this as the record of who verified whom, by what method, with what outcome and recording reference.

**Missing:**
- `IVerificationService` + implementation
- `VerificationController` (`POST /api/verification`, `GET /api/verification/{userId}`)
- Repository

---

### 6. No resource policies API

**Severity: Medium — table with no interface**

`resource_policies` exists in migration 003 (Layer 4). No controller exposes it. The document describes this as the mechanism for per-resource configuration: idle timeout, absolute max session length, 2FA requirements, allowed access hours. Without an API, none of these policies can be set or read.

**Missing:**
- `IResourcePolicyService` + implementation
- `ResourcePoliciesController` under `/api/apps/{appId}/resources/{resourceId}/policies`
- Repository

---

### 7. `ComplianceController` queries the database directly

**Severity: High — architecture violation**

`ComplianceController.cs:13` injects `IDbConnectionFactory` and executes Dapper INSERT/SELECT/UPDATE statements directly in the controller body. This violates the project's architecture rule: *"No direct DB access from the API layer."*

The consent and incident operations bypass the service and repository layers entirely. There is no business logic validation, no audit hook, and no testable surface.

**Fix required:** Extract to `IComplianceService` + `ConsentRepository` + `IncidentRepository`. The controller should only parse input, call the service, and return the response.

---

### 8. No role enforcement on `CompaniesController` and `AppsController`

**Severity: High — broken access control**

Both controllers have `[Authorize]` but no platform role check. Any authenticated user — including a standard `user` role — can:
- `POST /api/companies` — create a company
- `POST /api/apps` — register an application

The document states `registered_by` is always recorded and implies only privileged users can register apps and companies. There is currently no enforcement of this.

**Fix required:** Add a `platform_owner` role check to `POST /api/companies` and `POST /api/apps`. Pattern already exists in `AdminUserController`.

---

### 9. Identity model divergence — custom auth stack vs Supabase Auth

**Severity: Architectural — needs product decision**

The document states: *"`users.id` — Primary key — matches Supabase Auth uid"*

The code built a full custom authentication stack:
- GitHub OAuth → issues its own JWT
- Custom `sessions`, `refresh_tokens`, `api_tokens` tables
- HS256 JWTs issued by the platform, not Supabase

These are two different authentication architectures. The document's design assumes Supabase Auth is the identity provider and the platform references its UIDs. The code replaced Supabase Auth entirely.

This is not necessarily wrong, but it is a decision that should be explicitly agreed with the product owner. If the long-term direction is Supabase Auth, the current custom stack becomes migration debt. If the custom stack is the permanent choice, the document's note about Supabase Auth UIDs is incorrect and should be updated.

**Action required:** Confirm direction with product owner. Update schema document to reflect actual identity architecture.

---

## Priority Fix List

| # | Issue | Severity | File(s) |
|---|---|---|---|
| 1 | Add `last_seen_at` migration | High | `db/migrations/` — new migration |
| 2 | Relax `github_id NOT NULL` constraint | High | `db/migrations/` — new migration |
| 3 | Add role check to `CompaniesController` + `AppsController` | High | `CompaniesController.cs`, `AppsController.cs` |
| 4 | Refactor `ComplianceController` to service + repository | High | `ComplianceController.cs` + new service/repo |
| 5 | Add attendance events API | Medium | New controller, service, repository |
| 6 | Add verification events API | Medium | New controller, service, repository |
| 7 | Add resource policies API | Medium | New controller, service, repository |
| 8 | Rename `apps.created_at` → `apps.registered_at` | Low | `db/migrations/` — new migration |
| 9 | Confirm Supabase Auth vs custom auth direction | Architectural | Product owner decision required |

---

## What Claude Code Flagged — and Why It Was Right

The coder built 9 schema layers across 4 migrations before the product owner agreed on 5 of them. The result is:

- **Three tables exist with no API** — `verification_events`, `attendance_events`, `resource_policies`. The DB has the structure but no system can use it.
- **One controller violates architecture rules** — `ComplianceController` does direct DB access.
- **Design assumptions are now baked into the schema** — MFA methods, consent types, incident severity levels, and data classification labels are all hardcoded as text values in the schema. If the product owner disagrees with any of these in the next session, schema migrations are required rather than just spec updates.

The 7 agreed tables are well-implemented. Everything past that should be treated as a draft until the pending sessions confirm the design.
