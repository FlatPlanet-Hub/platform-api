# Bug Report — Linux App Service Provisioner & Startup Issues
**Date:** 2026-04-28
**Severity:** P1 (platform outage risk) + P2 (startup reliability)
**Status:** Fixed & deployed

---

## Background

Following a platform restart on 2026-04-27, both the FlatPlanet Platform API and the Security Platform became unresponsive (503/504). Investigation revealed two related root causes in how App Services were provisioned and configured.

---

## Bug 1 — Wrong Runtime Config on Provisioned App Services (P1)

**File:** `FlatPlanet.Platform.Infrastructure/Azure/AzureAppServiceProvisioner.cs`

### What was wrong

When the provisioner created a new Azure App Service, it set:

```csharp
WindowsFxVersion = "DOTNET|10.0"
NetFrameworkVersion = "v10.0"
```

These are **Windows-only** properties. The `FPPlatform` resource group uses a **Linux App Service Plan**. On Linux, setting `WindowsFxVersion` causes Azure to spin up a PHP/static container instead of a .NET runtime. The app never starts.

### Affected services

All 9 provisioned App Services in the FPPlatform resource group were affected to some degree. `infosec-training-api` was the most visible — developers were seeing `dotnet: not found` errors because the wrong container was loaded.

### Fix

```csharp
// Before
WindowsFxVersion = "DOTNET|10.0",
NetFrameworkVersion = "v10.0",

// After
LinuxFxVersion = "DOTNETCORE|10.0",
```

4 apps that had `LinuxFxVersion` missing were patched live via Azure CLI.

---

## Bug 2 — Missing `ASPNETCORE_URLS` on All Provisioned App Services (P1)

**File:** `FlatPlanet.Platform.Infrastructure/Azure/AzureAppServiceProvisioner.cs`

### What was wrong

The provisioner never injected `ASPNETCORE_URLS` into the app settings of provisioned services. Without this:

- .NET binds to port **5000** by default
- Azure's health probe checks port **8080**
- The probe never gets a response → Azure marks the container as unhealthy → crash loop
- Each failed probe waits ~230 seconds before retrying → app appears permanently down

This is what caused the full platform outage on 2026-04-27 after a restart.

### Affected services

All 9 App Services. Both core services (`flatplanet-api`, `flatplanet-security-api`) and all 7 provisioned project services.

`flatplanet-api` and `flatplanet-security-api` were fixed earlier in the session. The remaining 6 were patched in this session:
- `compliq-api`
- `fp-esignature-api`
- `iso-audit-readiness-api`
- `learning-management-system-program-api`
- `mayari-api`
- `online-competency-assessment-tool-api`

### Fix

Added to provisioner app settings dict:

```csharp
["ASPNETCORE_URLS"]        = "http://0.0.0.0:8080",
["ASPNETCORE_ENVIRONMENT"] = "Production",
```

Live services patched via Azure CLI (environment variable injection only — no restart required, takes effect on next cold start):

```bash
az webapp config appsettings set \
  --resource-group FPPlatform \
  --name <app-name> \
  --settings ASPNETCORE_URLS="http://0.0.0.0:8080" ASPNETCORE_ENVIRONMENT="Production"
```

---

## Bug 3 — Security Platform Startup CORS Query Has No Timeout (P2)

**File:** `src/FlatPlanet.Security.API/Program.cs` (Security Platform repo)

### What was wrong

At startup, the Security Platform runs a DB query to load allowed CORS origins:

```csharp
await Dapper.SqlMapper.QueryAsync<string>(tempConn, "SELECT DISTINCT base_url FROM apps ...");
```

This query had **no timeout**. If the database is slow or unreachable at boot time (e.g. after a platform restart when DB connections are saturating), the query hangs indefinitely. The entire app is blocked from starting with no error message — it just appears stuck.

### Fix

Wrapped in a `CommandDefinition` with a 10-second timeout:

```csharp
new Dapper.CommandDefinition(
    "SELECT DISTINCT base_url FROM apps WHERE status = 'active' ...",
    commandTimeout: 10)
```

If the query times out, the existing `catch` block handles it gracefully — the app boots with config-only CORS origins instead of DB origins.

---

## Bug 4 — No Post-Deploy Health Check in CI/CD (P2)

**Files:** `.github/workflows/deploy.yml` in both repos

### What was wrong

Neither repo's GitHub Actions workflow verified that the deployed app was actually healthy after deployment. A broken deploy (crash on startup, wrong config, bad build) would show as green in CI with no indication of failure.

### Fix

Added a health check step after each deploy:

```yaml
- name: Health check
  run: |
    echo "Waiting for app to warm up..."
    sleep 20
    curl --retry 6 --retry-delay 10 --retry-connrefused -f \
      https://<app-url>/health \
      || (echo "Health check failed — deployment may be broken" && exit 1)
```

- 20s initial wait for container warmup
- 6 retries × 10s = up to 80s window
- Fails the workflow if `/health` never returns 200

---

## Commits

| Repo | Commit | Description |
|---|---|---|
| `platform-api` | `7fda719` | Fix Linux provisioner config + post-deploy health check |
| `flatplanet-security-platform` | `d39339b` | Bound startup CORS query timeout + post-deploy health check |

---

## What to Watch

- **New provisions** going forward will get the correct Linux config automatically.
- **Existing 6 project App Services** have been patched with env vars. If any of them are redeployed by their own CI, the env vars will persist (Azure keeps appsettings separate from the deployment package).
- If a project team member manually recreates their App Service, they will need to re-run provisioning through the Platform API to get the correct config.
