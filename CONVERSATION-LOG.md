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

- [x] Suite 2–4 — ✅ ALL PASSED (2026-04-23, see session below)
- [ ] Auth portal URL — update `App.BaseUrl` in SP when portal is built
- [ ] Fix fp-development-hub GitHub branch in DB (`github_branch = 'master'`)

---

---

## Session: Project Deletion — Suites 2–4 Complete

**Date**: 2026-04-23
**Branch**: `main` (no new code — integration testing only)

---

### Integration Test Results

All remaining suites for the project deletion feature passed.

**Test subject**: Cash Flow v2  
- HubApi project ID: `7ff63aee-c9ad-4eda-920c-f426eddab98b`
- SP app ID: `ab20cdae-933c-4ed9-9243-b3ebf71a32e9`

| Suite | Description | Status |
|---|---|---|
| Suite 1 | `DELETE /api/projects/{id}` — HubApi soft-delete | ✅ PASSED (prev session) |
| Suite 2 | `DELETE /api/v1/apps/{id}` — SP hard delete | ✅ PASSED |
| Suite 3 | `POST /api/projects/{id}/sync-sp` — divergence recovery | ✅ PASSED |
| Suite 4a | SP app returns 404 post-delete | ✅ PASSED |
| Suite 4b | `app.delete` appears in SP admin audit log | ✅ PASSED |
| Suite 4c | Slug `cash-flow-v2` reusable after delete | ✅ PASSED |

**V26** (`db/V26__app_cascade_delete.sql`) was applied to Supabase by the user. This added the ON DELETE CASCADE/SET NULL FK rules that unblocked Suite 2.

---

### GAP-TEST-2 — platform_owner bypass missing on `/api/v1/authorize`

**Confirmed.** `AuthorizationService.AuthorizeAsync` in SP checks `user_app_roles` only. `platform_owner` JWT role claim is not checked — if no row exists in `user_app_roles` for that app, the response is `Allowed = false`.

**Effect on sync-sp**: Chris (platform_owner) got 403 because his roles on cash-flow-v2 were cleaned up during deactivation. Workaround: granted Chris owner role via SP's `POST /api/v1/apps/{appId}/users` (AdminAccess policy accepts platform_owner) to unblock the test.

**Coder action needed**: Add `platform_owner` bypass in `AuthorizationService.AuthorizeAsync` — check if user has `platform_owner` role claim before querying `user_app_roles`. Severity: P2.

---

### Minor SP Bug Noted

`POST /api/v1/apps` create response returns `registeredAt: 0001-01-01T00:00:00`. Value is stored correctly in DB — PUT and GET return the real timestamp. DTO not populated after INSERT. Low priority.

---

## Session: NetlifySiteUrl CORS + Retroactive Fix + Dynamic CORS Plan

**Date**: 2026-04-27
**Branches**: `main` (all commits direct to main this session)

---

### What Was Done

#### 1. Retroactive CORS fix — 7 provisioned App Services

Built and deployed `POST /api/admin/projects/{id}/sync-cors` endpoint:
- `UpdateCorsOriginAsync(appServiceName, allowedOrigin)` added to `IAzureAppServiceProvisioner` — GETs existing Azure app settings, merges `Cors__AllowedOrigins__0`, PUTs back (non-destructive)
- `SyncCorsAsync(projectId, userId)` added to `IProvisionAzureService` — same platform_owner auth guard as ProvisionAsync
- New DTO: `SyncCorsResponse(AppServiceName, AllowedOrigin, Message)`
- Committed: `feat: add sync-cors admin endpoint for retroactive CORS fix on provisioned App Services`

All 7 provisioned App Services updated successfully:
| Project | App Service | Origin set |
|---|---|---|
| CompliQ | compliq-api | https://fp-compliq.netlify.app |
| FP-ESignature | fp-esignature-api | https://fp-fp-esignature.netlify.app |
| InfoSec Training | infosec-training-api | https://fp-infosec-training.netlify.app |
| ISO Audit Readiness | iso-audit-readiness-api | https://fp-iso-audit-readiness.netlify.app |
| LMS | learning-management-system-program-api | https://fp-learning-management-system-program.netlify.app |
| Mayari | mayari-api | https://fp-mayari.netlify.app |
| Online Competency | online-competency-assessment-tool-api | https://fp-online-competency-assessment-tool.netlify.app |

#### 2. InfoSec Training developer CORS issue

Investigated `Failed to fetch` / 405 on OPTIONS preflight. Confirmed Linux App Service (no WebDAV). Conclusion: 405 on OPTIONS on Linux = app startup crash, not a CORS config issue. Advised developer to check Azure Log Stream for startup exception. Pending: developer to send back log output.

#### 3. Bathala CORS in Security Platform

`https://fp-bathala.netlify.app` was not in the Security Platform's allowed origins. Updated `apps.base_url` for Bathala (`id: 1a2dc149-14bf-4ae7-a2f6-091a5619a461`) to `https://fp-bathala.netlify.app` via `PUT /api/v1/apps/{id}` using Chris Moriarty's JWT. Security Platform App Service requires manual restart to pick up (startup-only CORS).

#### 4. Dynamic CORS — Phase 9 planned (NOT YET BUILT)

Confirmed via `docs/phase-8-spec-gaps.md` that dynamic CORS was always deferred to Phase 9. Plan created:

**3 new files:**
- `Application/Interfaces/Services/IDynamicCorsService.cs`
- `Infrastructure/Cors/DynamicCorsService.cs` — `IMemoryCache`, 60s TTL, DB fallback to config
- `API/Cors/DynamicCorsPolicyProvider.cs` — implements `ICorsPolicyProvider`, validates Origin per-request

**2 files to edit:**
- `Program.cs` — replace startup CORS block with `AddCors()` + register services
- `AppService.cs` — inject `IDynamicCorsService`, call `InvalidateCache()` after create/update/delete

**Branch:** `feature/phase-9-dynamic-cors`  
**Status:** Planned, ready for Cloud to implement tomorrow.

---

### Open Items for Next Session

| # | Item | Repo | Notes |
|---|---|---|---|
| 1 | Build Phase 9 dynamic CORS | `flatplanet-security-platform` | Plan is done — Cloud to implement on `feature/phase-9-dynamic-cors` |
| 2 | InfoSec Training startup crash | InfoSec-Training backend | Developer needs to send Azure Log Stream output |
| 3 | Tifa docs update | `FlatPlanetHubApi` | Tifa agent hit token limit mid-task — resume: update README + CHANGELOG for v1.7.0 (netlifySiteUrl field, sync-cors endpoint, rate limit 1000/min) |
| 4 | SP bug: `platform_owner` bypass missing in `AuthorizeAsync` | `flatplanet-security-platform` | P2 — Chris got 403 when no row in user_app_roles |

---

---

## Session: DB Connection Pool Fix

**Date**: 2026-05-14
**Branch**: `claude/eager-shirley-b8bc72` → merged to `main` via PRs #34–#37

---

### What Was Done

Fixed persistent cold-start timeouts on the Platform API (`flatplanet-api` Azure App Service). Clients were seeing `[platformApi] still timing out — final retry` and `AbortError: signal is aborted without reason` across multiple frontend apps.

**Root cause of original PR #33 failure:**
PR #33 added `Connection Timeout=10` to the Npgsql connection string. On a cold Azure App Service, the Supabase SSL handshake takes >10s — every `OpenAsync()` call threw, taking the entire app down. Health check also started probing DB with `SELECT 1`, causing deploy pipeline failures (curl returned 502 during restart window).

**PRs merged this session:**

| PR | Change | Outcome |
|---|---|---|
| #34 | `Minimum Pool Size=0→1` + keepalive params | Broke — TCP socket keepalives (`Tcp Keepalives Idle/Interval/Retries`) rejected by Azure Linux containers with `setsockopt` error |
| #35 | Remove TCP socket keepalives + remove health check from deploy pipeline | Fixed socket error; deploy pipeline no longer fails |
| #36 | Align with SP settings: `Max Auto Prepare=0`, `Command Timeout=30`, `Timeout=30`, `Max Pool Size=10→20`, add `Keepalive=30` | Broke — `Keepalive=30` with PgBouncer transaction mode caused pool connections to block, hanging all requests for 230s |
| #37 | Remove `Keepalive=30`, revert `Minimum Pool Size=1→0` | Fixed — same conclusion reached by SP team independently |

**Final settled connection string settings:**
```
Minimum Pool Size=0        — connections open on demand, close naturally
Maximum Pool Size=20
Max Auto Prepare=0         — required for PgBouncer transaction mode (port 6543)
Command Timeout=30         — per-query execution limit
Timeout=30                 — connection open timeout (safe at 30s, unlike the 10s that killed PR #33)
No Reset On Close=true
SSL Mode=Require
Trust Server Certificate=true
```

**Lessons learned:**
- `Minimum Pool Size > 0` → background maintenance loops burning thread pool threads when Supabase drops idle connections. Don't use it.
- `Keepalive=30` → incompatible with PgBouncer transaction mode. Keepalive queries compete for backend connections and can block the pool.
- `Connection Timeout=10` → too aggressive for cold Azure + Supabase. Leave at default (15s) or set to 30s.
- `Tcp Keepalives Idle/Interval/Retries` → Linux socket-level options, rejected by Azure App Service Linux containers.
- Deploy health check (`curl -f /health`) → fires during Azure restart window, always gets 502. Removed from pipeline.

**Deploy incident:**
Multiple rapid deploys today left Azure App Service with a stuck Kudu deployment lock. Cleared via: Kudu Advanced Tools → Debug Console → `rm /home/site/locks/deployment.lock`. Final deploy done manually.

---

### Open Items for Next Session

| # | Item | Repo | Notes |
|---|---|---|---|
| 1 | Build Phase 9 dynamic CORS | `flatplanet-security-platform` | Plan is done — Cloud to implement on `feature/phase-9-dynamic-cors` |
| 2 | InfoSec Training startup crash | InfoSec-Training backend | Developer needs to send Azure Log Stream output |
| 3 | Tifa docs update | `FlatPlanetHubApi` | Resume: update README + CHANGELOG for v1.7.0 (netlifySiteUrl field, sync-cors endpoint, rate limit 1000/min) |
| 4 | SP bug: `platform_owner` bypass missing in `AuthorizeAsync` | `flatplanet-security-platform` | P2 — Chris got 403 when no row in user_app_roles |

---

## Session: Rate Limit — Token-Type Differentiation (Wayfinder Override Revert)

**Date**: 2026-09-07
**Branch**: `fix/rate-limit-token-type-differentiation` (off `main`)

---

### What Was Done

Reverted the Wayfinder-specific per-project rate-limit override added in commit `39b736f` (`RateLimitOverrides` dictionary keyed by projectId, hardcoded 200/min per-user + 3000/min per-project for Wayfinder's projectId) in favor of a general, token-type-differentiated ceiling in `FlatPlanet.Platform.API/Program.cs`.

**Why the override was dead weight:** the Wayfinder rate-limit issue that prompted it turned out to be a write-permission bug on SP's `user` role — not an actual ceiling problem. But the underlying architecture reason it *looked* like a rate-limit issue is real: an app authenticating with one shared `service_token`/`api_token` on behalf of N concurrent users collapses all of them onto a single bucket at the per-project and per-(project,user) layers.

**Fix:** ceilings now derive from the JWT's `token_type` claim (`GetRateLimitTokenType()` helper), not from a per-projectId hardcode:
- `user_token` (unchanged): 1000/min per-user, 500/min per-project, 40/min per (project, user).
- `service_token` / `api_token` (new default, every app): 1000/min per-user (unchanged), 1500/min per-project, 150/min per (project, user) — revised down from an initial 3000/500 pairing after review found the 20-connection Supabase pool (tuned for PgBouncer transaction mode) couldn't safely absorb it; 1500/150 sits below the pool's realistic sustainable throughput instead. `api_token` is the only token type actually stamped today (`JwtService.cs`); `service_token` is reserved for future work.
- Missing/unrecognized `token_type` → falls back to the strict `user_token` tier (fail closed).

Every FlatPlanet app with a service/api token gets the higher ceiling automatically; zero client changes, zero per-app configuration going forward.

Also updated `docs/frontend-sp-resilience-guide.md` §7 (was stale — still described the pre-PR#39/40 100/min-per-project single-layer model) and `CHANGELOG.md` (`[Unreleased]`).

---

### Follow-up: Pool-Size Risk (Lightning Review)

**Date**: 2026-09-07 (same session, same branch)

Lightning's review flagged that the initial service-token ceilings (3000/min per-project, 500/min per (project,user)) were sized to Wayfinder's traffic pattern, not to what the DB connection pool can actually sustain. `SupabaseSettings.cs`'s `Maximum Pool Size=20` was deliberately tuned for Supabase's PgBouncer transaction-mode limits — bumping the pool to fit the rate limit was rejected as unsafe.

**Fix:** revised the ceilings down instead of raising the pool:
- Per-project (Layer 2, service/api_token): **1500/min** (was 3000).
- Per-(project,user) (Layer 3, service/api_token): **150/min** (was 500).
- `user_token` limits unchanged (500/project, 40/(project,user)).

Rationale (now in `Program.cs`): 20 pool connections × 60s ÷ ~200ms average query ≈ 6000/min theoretical max; realistic sustainable throughput under bursts is more like 1500-2000/min, so 1500/min sits comfortably below the pool ceiling.

Also:
- Fixed the stale comment in `SupabaseSettings.cs` (said "per-project rate limiting at 100/min", which was already outdated before this session) and added a cross-reference so `Program.cs`'s ceilings and the pool size don't drift apart independently.
- Corrected `service_token` doc accuracy across `Program.cs`, `CHANGELOG.md`, and this log: `JwtService.cs` only ever stamps `token_type: "api_token"` — no code path stamps `service_token` today. Wording now says `api_token` gets the higher tier today; `service_token` is reserved for future work and will hit the same tier once it exists.
- Updated `docs/frontend-sp-resilience-guide.md` §7's table to 1500/150.

Verified with `dotnet build FlatPlanet.Platform.slnx` (clean) and a grep pass confirming 1500/150 (not 3000/500) appear consistently in `Program.cs`, `CHANGELOG.md`, this log, and the resilience guide.

---
