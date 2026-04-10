# HubApi — Bug Fix Backlog

---

## BUG-01 — SecurityPlatformService: bare `EnsureSuccessStatusCode()` masks SP errors

**Severity:** P1
**Status:** Open
**File:** `FlatPlanet.Platform.Infrastructure/ExternalServices/SecurityPlatformService.cs`

**Problem:**
All 8+ calls to `response.EnsureSuccessStatusCode()` throw `HttpRequestException` on failure.
`GlobalExceptionMiddleware` has no handler for `HttpRequestException` — it falls to the `_ => 500` catch-all.
The actual SP error (status code + body) is completely masked. Callers see `"An unexpected error occurred."` with no useful info.

**Fix:**
Replace every bare `EnsureSuccessStatusCode()` call with:

```csharp
if (!response.IsSuccessStatusCode)
{
    var body = await response.Content.ReadAsStringAsync();
    throw new InvalidOperationException($"Security Platform error: {(int)response.StatusCode} — {body}");
}
```

Apply this consistently to all call sites in `SecurityPlatformService.cs`.
`InvalidOperationException` is already mapped to `409` in `GlobalExceptionMiddleware` — this surfaces the real SP error message to the caller.

**Call sites to fix (lines in current develop):**
- Line 35 — `RegisterAppAsync`
- Line 69 — permission creation loop
- Line 91 — role creation loop
- Line 114 — role-permission assignment loop
- Line 125 — `GrantRoleAsync`
- Line 134 — `ChangeRoleAsync`
- Line 140 — `RevokeRoleAsync`
- Line 154 — (check remaining)

**Branch:** `fix/sp-service-error-surfacing` (branch from `main`)
**Target:** `main` — hotfix, sync back to `develop` after merge

---

## BUG-02 — Service token mismatch between HubApi and SP (suspected)

**Severity:** P1
**Status:** Needs verification

**Problem:**
`GrantRoleAsync` calls SP using the service token configured in HubApi's Azure App Config (`SecurityPlatform__ServiceToken`).
If this value doesn't match what SP has configured as its valid service token, SP returns `401` — which is then masked by BUG-01.

**Fix:**
1. Go to **HubApi** Azure App Service → Configuration → `SecurityPlatform__ServiceToken` — copy the value
2. Go to **SP** Azure App Service → Configuration → find the service token setting — compare
3. If mismatched, update HubApi's value to match SP's

This is a config fix — no code change needed if values match after BUG-01 is deployed and the real error is visible.

---

## BUG-04 — `CreateProjectAsync` inserts DB row before SP registration — orphaned rows on failure

**Severity:** P1
**Status:** Open
**File:** `FlatPlanet.Platform.Application/Services/ProjectService.cs`

**Problem:**
`CreateProjectAsync` inserts the project into `platform.projects` (line 45) before calling SP.
If `RegisterAppAsync` throws (e.g. slug conflict → 409), the DB row is never rolled back.
Every retry creates a new orphaned row with a new UUID and no `AppId`/`AppSlug`.
These rows are invisible in project listings (no SP app linked) but accumulate in the DB.

**Fix — reorder operations: SP first, DB insert only on success:**

```csharp
public async Task<ProjectResponse> CreateProjectAsync(...)
{
    var shortId    = Guid.NewGuid().ToString("N")[..8];
    var schemaName = $"project_{shortId}";
    var appSlug    = GenerateSlug(request.Name);

    // 1. Register with SP first — if this fails, nothing is persisted
    var appId = await _securityPlatform.RegisterAppAsync(request.Name, appSlug, baseUrl, companyId);
    await _securityPlatform.SetupProjectRolesAsync(appId);
    await _securityPlatform.GrantRoleAsync(appId, userId, "owner");

    // 2. Only insert DB row after SP succeeds
    var project = new Project
    {
        Id         = Guid.NewGuid(),
        Name       = request.Name,
        Description = request.Description,
        SchemaName = schemaName,
        AppId      = appId,
        AppSlug    = appSlug,
        OwnerId    = userId,
        TechStack  = request.TechStack,
        IsActive   = true,
        CreatedAt  = DateTime.UtcNow,
        UpdatedAt  = DateTime.UtcNow
    };

    var created = await _projectRepo.CreateAsync(project);

    _ = _gitHubRepo.SeedProjectFilesAsync(created);
    _ = _dbProxy.CreateSchemaAsync(schemaName);

    return ToResponse(created, "owner");
}
```

Note: The `UpdateAsync` call is also removed — `AppId` and `AppSlug` are now set before the insert,
so a second DB round-trip is no longer needed.

**Data cleanup — run in HubApi Supabase DB:**
```sql
DELETE FROM platform.projects WHERE app_id IS NULL;
```
Verify first with `SELECT` before deleting.

**Branch:** `fix/create-project-sp-first` (branch from `main`)
**Target:** `main` — hotfix, sync back to `develop` after merge

---

## BUG-03 — New project members not auto-granted `dashboard-hub` viewer access

**Severity:** P1
**Status:** Open
**File:** `FlatPlanet.Platform.Application/Services/ProjectMemberService.cs`

**Problem:**
`InviteMemberAsync` grants the user a role on the project app (e.g. `fp-development-hub`) but never grants
them access to `dashboard-hub`. The authorize gate on `dashboard-hub` checks for `view_projects` permission
— users with no `dashboard-hub` role fail the check and cannot log in to the dashboard.

**Decisions:**
- Removal does NOT revoke dashboard access — user keeps viewer, just sees no projects
- Auto-grant must not downgrade an existing dashboard role (e.g. don't overwrite `admin` with `viewer`)

**Files to change:**

### 1. `ISecurityPlatformService.cs` — add method

```csharp
Task<Guid?> GetAppIdBySlugAsync(string slug);
```

### 2. `SecurityPlatformService.cs` — implement

`GET api/v1/apps/slug/{slug}` does not exist in SP. Use `GET api/v1/apps` and filter client-side:

```csharp
public async Task<Guid?> GetAppIdBySlugAsync(string slug)
{
    var result = await ServiceClient.GetFromJsonAsync<SpResponse<IEnumerable<SpAppIdData>>>(
        "api/v1/apps");
    return result?.Data?.FirstOrDefault(a => a.Slug == slug)?.Id;
}
```

Update `SpAppIdData` record to include `Slug`:

```csharp
private sealed record SpAppIdData(
    [property: JsonPropertyName("id")]   Guid   Id,
    [property: JsonPropertyName("slug")] string Slug);
```

### 3. `ProjectMemberService.InviteMemberAsync` — auto-grant logic

After `GrantRoleAsync` for the project (line 65), add:

```csharp
// Auto-grant viewer on dashboard-hub — do not downgrade existing role
try
{
    var dashboardAppId = await _securityPlatform.GetAppIdBySlugAsync("dashboard-hub");
    if (dashboardAppId is not null)
    {
        var access = await _securityPlatform.GetUserAppAccessAsync(request.UserId);
        var hasRole = access.Any(a =>
            a.AppSlug == "dashboard-hub" && !string.IsNullOrEmpty(a.RoleName));
        if (!hasRole)
            await _securityPlatform.GrantRoleAsync(dashboardAppId.Value, request.UserId, "viewer");
    }
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to auto-grant dashboard-hub viewer for user {UserId}", request.UserId);
}
```

Failure to grant dashboard access must NOT roll back the project grant — hence the try/catch.

**Branch:** `fix/auto-grant-dashboard-viewer` (branch from `main`)
**Target:** `main` — hotfix, sync back to `develop` after merge

---
