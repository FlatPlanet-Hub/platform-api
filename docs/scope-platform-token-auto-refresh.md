# Scope: Platform API Token Auto-Refresh

**Status:** Pending — not started  
**Created:** 2026-05-13  
**Priority:** Medium (immediate pain: esignature token expires 2026-05-15)

---

## Problem

Every deployed project app has `PlatformApi__Token` in Azure App Settings — a JWT that expires. When it expires, someone must manually regenerate + update Azure. At scale (27+ projects), this is unsustainable.

---

## Root Cause

Apps use a **JWT** (time-limited) as their permanent credential to call HubApi. JWTs expire by design. There is no rotation mechanism.

---

## Proposed Architecture

Replace the JWT credential with a **permanent service token** (non-expiring random secret). Apps use it to fetch a fresh JWT on demand.

```
Current:  App → HubApi  (JWT, expires → breaks)

Proposed: App → HubApi /api/apps/me/token  (ServiceToken, permanent)
                    ↓
               returns fresh JWT
                    ↓
          App → HubApi  (JWT, auto-refreshed)
```

---

## What Gets Built

### HubApi — 4 changes

| # | Change | Detail |
|---|---|---|
| 1 | New DB table `project_service_tokens` | `id`, `project_id`, `token_hash`, `name`, `created_at`, `revoked` — no `expires_at` |
| 2 | Generate service token at provisioning | Random 64-char secret, store hashed, return plaintext once (like GitHub PAT) |
| 3 | New endpoint `GET /api/apps/me/token` | Auth via `X-Service-Token` header → validates hash → returns current JWT, auto-generates if expired |
| 4 | Hub UI: show service token on project page | "Copy Service Token" button, one-time reveal |

### Each Project App — 2 changes

| # | Change | Detail |
|---|---|---|
| 1 | Startup: call `/api/apps/me/token` | Use `PlatformApi__ServiceToken` (env var) → cache the JWT |
| 2 | 401 handler: auto-refresh | On 401 from HubApi, re-call token endpoint, retry once |

### Azure App Settings — per project

| Remove | Add |
|---|---|
| `PlatformApi__Token` (expiring JWT) | `PlatformApi__ServiceToken` (permanent, set once) |

---

## Migration Plan

1. **HubApi PR** — build the table + endpoint + provisioning hook
2. **Generate service tokens** for all 27 existing projects via a one-time migration endpoint
3. **Update each project app** with the startup token-fetch pattern
4. **Push service tokens to Azure** per project (one-time manual step, then never again)
5. **Remove old `PlatformApi__Token`** from Azure App Settings after all apps updated

---

## Effort Estimate

| Part | Effort |
|---|---|
| HubApi: table + endpoint + provisioning | ~1 day |
| Migration: generate tokens for 27 projects | ~2 hrs |
| Per project app changes (startup + 401 retry) | ~2 hrs each |
| Azure App Settings update (27 projects) | ~1 hr scripted via Azure CLI |

**Total: ~2-3 days** across HubApi + all project apps.

---

## Immediate Action (unblocks esignature)

While this feature is being built, manually fix fp-esignature-api:
1. Azure Portal → `fp-esignature-api` → Environment variables → update `PlatformApi__Token`
2. Regenerate the Claude token in FlatPlanet Hub (gets 365-day expiry)
3. Update Azure with the new token
