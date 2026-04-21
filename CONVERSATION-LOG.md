# Conversation Log — FlatPlanet Platform API (HubApi)

---

## Session: Dataverse Integration

**Date**: 2026-04-21
**Branch**: `feature/feat-dataverse-integration` → merged to `main` via PR #23

---

### What Was Done

#### 1. Built Dataverse proxy

Two new endpoints added under `DataverseController`:
- `GET /api/v1/dataverse/employees` — active Round Earth Philippines employees
- `GET /api/v1/dataverse/accounts` — client accounts

Token fetched from existing Azure Function (`GetCrmToken`), cached 55 min via `IMemoryCache`.

**Employee fields returned**: `name`, `employmentDate`, `separationDate`, `employmentStatus`, `clientOpsLead`, `client`
**Server-side filters**: `statecode = 0` + `_fp_company_value = bd7c35ae-b482-e911-a83a-000d3a07f6fe` (Round Earth Philippines, Inc.)

#### 2. Bugs fixed during testing

| Bug | Fix |
|---|---|
| Spaces in `$filter` caused `UriFormatException` → 500 | URL-encoded: `statecode%20eq%200` |
| `_fp_reportingto_value` field doesn't exist | Corrected to `_fp_activereportingto_value` |
| `accounts?$select=fp_name` — field doesn't exist | Corrected to `name` (standard OData field) |
| Company filter missing | Added `_fp_company_value eq bd7c35ae...` |

#### 3. Azure config required

`Dataverse__TokenFunctionKey` must be set in the `flatplanet-api` App Service configuration.

#### 4. Docs updated

`docs/platform-api-reference.md` bumped to v1.5.0 — full Dataverse section added.

#### 5. Key commits

| Commit | Message |
|---|---|
| `f8192f4` | feat: add Dataverse proxy integration — employees and accounts endpoints |
| `6f24f64` | fix: URL-encode spaces in OData filter |
| `c084745` | fix: correct Client Ops Lead field name |
| `a206377` | fix: correct accounts field name |
| `e88218a` | fix: filter employees to Round Earth company only |
| `22cf3e2` | docs: API reference v1.5.0 |

---

### Decisions Made

| Decision | Rationale |
|---|---|
| Proxy in HubApi (not per-app direct) | One credential set, shared token cache |
| Token cached 55 min | Tokens expire ~60 min; 5-min buffer prevents stale calls |
| No filtering params on endpoints | Raw data returned — consuming apps own business logic |
| Company filter hardcoded server-side | Only Round Earth Philippines data needed |

---

---

## Session: Project Deletion Feature + Dataverse Field Addition

**Date**: 2026-04-21
**Branch**: `main` (all commits direct)
**PRs**: #24 (project deletion)

---

### What Was Done

#### 1. Project deletion feature (PR #24)

Full soft-delete pipeline from HubApi through to the Security Platform (SP).

**`ProjectService.DeactivateProjectAsync`** — updated:
- Renames `name` → `{name} (deleted)`
- Renames `appSlug` → `{slug}-deleted-{yyyyMMddHHmmssfff}` (millisecond timestamp — prevents collision on re-use)
- Sets `is_active = false`
- Calls SP to deactivate the app **best-effort** — logs on failure, never throws (SP down must not block HubApi deactivation)

**`ProjectService.SyncSpStatusAsync`** — new method:
- Auth check: `manage_members` permission required
- Guards: `IsActive` must be false, `AppId` must not be null, `AppSlug` must not be null
- Calls `DeactivateAppAsync` to re-sync SP when it diverged from HubApi

**`ISecurityPlatformService.DeactivateAppAsync`** — new interface method (with XML docs)

**`SecurityPlatformService.DeactivateAppAsync`** — new implementation:
- `PUT /api/v1/apps/{appId}` on SP with mutated name, slug, `status = inactive`
- Does not send `baseUrl` (preserves existing)

**`POST /api/projects/{id}/sync-sp`** — new endpoint in `ProjectController`

**`IProjectService.SyncSpStatusAsync`** — interface updated

#### 2. Unit test fix

`ProjectServiceTests.CreateSut()` was missing `ILogger<ProjectService>` after the `ProjectService` constructor was updated. Fixed by adding `Mock<ILogger<ProjectService>>` and passing `_logger.Object`.

#### 3. Dataverse — `fp_activeclientofficer` field added

`fp_activeclientofficer` added to:
- `DataverseService.GetEmployeesAsync` — added to `$select` query string
- `EmployeeDto` — added as `string? ActiveClientOfficer` (nullable — not all employees have this set)

#### 4. Key commits

| Commit | Message |
|---|---|
| `d5ada95` | feat: rename HubApi slug and SP app on project deactivation |
| `286ea40` | fix: P1/P2 review findings — millisecond timestamp, null-forgiving cleanup |
| `87599b7` | feat: add POST /api/projects/{id}/sync-sp for SP divergence recovery |
| `3f475c8` | fix: add manage_members auth guard to SyncSpStatusAsync (P1) |
| `977df1e` | fix: restore AppSlug null guard in SyncSpStatusAsync |
| `b61b8b6` | feat: rename HubApi slug and SP app on project deactivation (PR #24) |
| `0fd69d4` | fix: update ProjectServiceTests to pass ILogger to constructor |
| `07f73b8` | feat: add fp_activeclientofficer to Dataverse employee query |

---

### Integration Testing Status

Test subject: **Cash Flow v2**
- HubApi project ID: `7ff63aee-c9ad-4eda-920c-f426eddab98b`
- SP app ID: `ab20cdae-933c-4ed9-9243-b3ebf71a32e9`

| Suite | Description | Status |
|---|---|---|
| Suite 1 | Deactivate via `DELETE /api/projects/{id}` | ✅ PASSED |
| Suite 2 | SP hard delete via `DELETE /api/v1/apps/{id}` | 🔴 BLOCKED — see SP notes |
| Suite 3 | Sync-SP recovery via `POST /api/projects/{id}/sync-sp` | ⏳ Pending Suite 2 |
| Suite 4 | Edge cases (duplicate slug, audit log) | ⏳ Pending |

Suite 1 results confirmed:

| Field | Expected | Actual |
|---|---|---|
| `name` | `Cash Flow v2 (deleted)` | ✅ |
| `appSlug` | `cash-flow-v2-deleted-20260421071811284` | ✅ |
| `isActive` | `false` | ✅ |
| SP `slug` | matches HubApi | ✅ |
| SP `status` | `inactive` | ✅ |

Suite 2 is blocked on the **SP side** (not HubApi) — see SP `CONVERSATION-LOG.md` for details. Once `V26__app_cascade_delete.sql` is applied to Supabase, Suite 2 can be retried.

---

### Decisions Made

| Decision | Rationale |
|---|---|
| Millisecond timestamp suffix | Prevents slug collision if project is deactivated, restored, and deactivated again |
| SP call is best-effort | HubApi deactivation must not fail if SP is unavailable; `sync-sp` endpoint handles recovery |
| Separate `SyncSpStatusAsync` endpoint | Ops recovery path — allows re-syncing SP after divergence without re-deactivating in HubApi |

---

### Open Items

- [ ] Suite 2–4 pending — resume after `V26__app_cascade_delete.sql` applied on SP side
- [ ] Auth portal URL — update `App.BaseUrl` in SP when portal is built
- [ ] Fix fp-development-hub GitHub branch in DB (`github_branch = 'master'`)

---
