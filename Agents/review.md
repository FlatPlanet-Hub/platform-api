# Fix Plan — FlatPlanet Hub API

## How This File Works

This is the **living fix list**. Every time the reviewer finds new issues they get added here. The coder works from this file exclusively.

**Rules:**
- Reviewer always updates this file when new issues are found — no exceptions
- Coder reads this file before starting any fix work
- Each fix gets a commit message listed here — use it exactly
- Mark nothing as done here — the reviewer verifies and removes completed items after re-review
- Database fixes go in a new numbered migration file under `db/migrations/` — never edit existing ones

---

## Current Status

Round 1 fixes: all complete.

- FIX 1 — ✅ Done (committed prior to round 1 review)
- FIX 2 — 🔀 Wrong repo — belongs to `flatplanet-security-platform` (see below)
- FIX 3 — ✅ Done (commit `65d2d22`)

---

## FIX 2 — PENDING (Security Platform repo — not HubApi)

**Why this is here:** The root cause is in the Security Platform, not HubApi. Filing here for visibility until it is actioned in the SP repo.

**Problem:** `SecurityPlatformService.GetUserAppAccessAsync` calls `GET /api/v1/users/{userId}` and maps the response to `SpAppAccessDto[]`. `SpAppAccessDto.Permissions` always deserializes as `[]` because the SP's `UserAppAccessDto` has no `Permissions` property.

Every Claude Code API token is issued with zero permissions. All DB proxy calls (`read`, `write`, `ddl`) fail with 403.

**Fix — Security Platform repo, two files:**

**File 1:** `src/FlatPlanet.Security.Application/DTOs/Users/UserDetailResponse.cs`

Add `Permissions` to `UserAppAccessDto`:
```csharp
public class UserAppAccessDto
{
    public Guid AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string AppSlug { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string[] Permissions { get; set; } = [];   // ← ADD THIS
    public string Status { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
```

**File 2:** `src/FlatPlanet.Security.Application/Services/UserService.cs` — `GetByIdAsync`

Populate `Permissions` when building `UserAppAccessDto` entries via a join on `role_permissions → permissions`. `UserAppRoleDetail` must also carry `Permissions string[]`.

**Acceptance:** `GET /api/v1/users/{id}` returns `app_access[].permissions` as a string array (e.g. `["read","write","ddl"]`). A generated Claude Code token carries the correct permissions and can execute read/write/DDL operations.

**Commit message (SP repo):**
```
fix: SP UserAppAccessDto — add Permissions to user app access response so Claude Code tokens carry correct permissions
```

---

## Commit Convention

```
fix: MigrationController — remove extra userId arg from SyncDataDictionaryAsync call (compile error)
fix: SP UserAppAccessDto — add Permissions to user app access response so Claude Code tokens carry correct permissions
fix: SeedProjectFilesAsync — use Content API instead of low-level tree API to support repos with existing commits
```
