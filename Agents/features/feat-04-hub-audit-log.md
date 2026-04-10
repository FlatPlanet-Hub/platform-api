# FEAT-04 — HubApi Audit Log

**Repo:** FlatPlanetHubApi (platform-api)
**Branch:** `feature/feat-04-hub-audit-log`
**Depends on:** FEAT-02 (HubApi migration `008_platform_audit_log.sql`)
**Coder:** HubApi coder

---

## Goal

Log every admin write operation in HubApi — create project, deactivate project,
create API token, revoke API token.
ISO 27001 A.12.4.1.

---

## Migration

File: `db/migrations/008_platform_audit_log.sql`

```sql
CREATE TABLE IF NOT EXISTS platform.audit_log (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    actor_id    UUID        NOT NULL,
    actor_email TEXT        NOT NULL,
    action      TEXT        NOT NULL,   -- 'project.create', 'project.deactivate', 'token.create', 'token.revoke'
    target_type TEXT        NOT NULL,
    target_id   UUID,
    details     JSONB,
    ip_address  TEXT,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_audit_log_actor  ON platform.audit_log(actor_id);
CREATE INDEX IF NOT EXISTS idx_audit_log_time   ON platform.audit_log(created_at DESC);

REVOKE UPDATE, DELETE ON platform.audit_log FROM PUBLIC;
```

---

## Conventions

- Follow existing `ProjectRepository` pattern: `IDbConnectionFactory`, Dapper, `await using var conn`
- Actor resolved from JWT claims in controller, passed down to service
- Never throw on audit failure — wrap in try/catch. On catch:
  - `_logger.LogError(ex, "AUDIT FAILURE: {Action} on {TargetType} {TargetId}", action, targetType, targetId)`
  - Surfaces in Azure Application Insights / Log Stream — never silently drop records
  - Never rethrow — audit failure must not break the main request

---

## Files to Create

### Interface

`FlatPlanet.Platform.Application/Interfaces/IAuditLogRepository.cs`
```csharp
public interface IAuditLogRepository
{
    Task LogAsync(Guid actorId, string actorEmail, string action,
                  string targetType, Guid? targetId, object? details, string? ipAddress);
    Task<IEnumerable<AuditLogDto>> GetPagedAsync(int page, int pageSize,
                  Guid? actorId, DateTime? from, DateTime? to);
    Task DeleteExpiredAsync(int retentionDays);
}
```

### AuditLogDto

`FlatPlanet.Platform.Application/DTOs/AuditLogDto.cs`
```csharp
public class AuditLogDto
{
    public Guid     Id         { get; set; }
    public string   ActorEmail { get; set; } = string.Empty;
    public string   Action     { get; set; } = string.Empty;
    public string   TargetType { get; set; } = string.Empty;
    public Guid?    TargetId   { get; set; }
    public DateTime CreatedAt  { get; set; }
}
```

### Repository

`FlatPlanet.Platform.Infrastructure/Repositories/AuditLogRepository.cs`

- Constructor: `IDbConnectionFactory _db, ILogger<AuditLogRepository> _logger`
- `LogAsync`: INSERT into `platform.audit_log`, serialize `details` with `JsonSerializer.Serialize()`
- `GetPagedAsync`: paginated SELECT with optional filters on `actor_id`, `created_at`
- `DeleteExpiredAsync`: DELETE rows older than retention window
- Wrap `LogAsync` in try/catch — on catch: `_logger.LogError(...)`, never rethrow

### Getting ip_address in Controllers

Read from `HttpContext` and pass to service → repository:
```csharp
var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
```
Pass `ip` as parameter through the service method call chain.

### Action Constants

`FlatPlanet.Platform.Application/Common/AuditAction.cs`
```csharp
public static class AuditAction
{
    public const string ProjectCreate     = "project.create";
    public const string ProjectDeactivate = "project.deactivate";
    public const string TokenCreate       = "token.create";
    public const string TokenRevoke       = "token.revoke";
}
```

---

## Files to Modify

### ProjectService

Add `IAuditLogRepository _auditLog` to constructor.

| Method | Action | Details |
|---|---|---|
| `CreateAsync` | `project.create` | `{ name, appSlug }` |
| `DeactivateAsync` | `project.deactivate` | `{ projectId, name }` |

Pass `actorId` and `actorEmail` as parameters from controller (read from JWT claims).

### ApiTokenService

Add `IAuditLogRepository _auditLog` to constructor.

| Method | Action | Details |
|---|---|---|
| `CreateAsync` | `token.create` | `{ tokenId, name }` — never log the token itself |
| `RevokeAsync` | `token.revoke` | `{ tokenId }` |

---

## New Endpoint

`FlatPlanet.Platform.API/Controllers/AuditLogController.cs`

- Route: `api/platform/audit-log`
- Auth: `[Authorize]` + check `view_all_projects` permission (reuse existing pattern)
- `GET /` — query params: `page`, `pageSize`, `actorId`, `from`, `to`
- Returns paginated list: `{ id, actorEmail, action, targetType, targetId, createdAt }`

---

## Audit Log Retention

HubApi does not have a `security_config` table. Use `appsettings.json`:

```json
"AuditLog": {
  "RetentionDays": 1095
}
```

Add a `BackgroundService` that runs once daily and deletes old rows:

```csharp
public class AuditLogCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;

    public AuditLogCleanupService(IServiceScopeFactory scopeFactory, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _config       = config;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope    = _scopeFactory.CreateScope();
            var auditLog       = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            var days           = _config.GetValue<int>("AuditLog:RetentionDays", 1095);
            await auditLog.DeleteExpiredAsync(days);
            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }
}
```

> Note: `IConfiguration` is singleton so it can be constructor-injected directly into the `BackgroundService`.
> `IAuditLogRepository` is scoped — resolve it per iteration via `IServiceScopeFactory`.

Add to `IAuditLogRepository`:
```csharp
Task DeleteExpiredAsync(int retentionDays);
```
```sql
DELETE FROM platform.audit_log
WHERE created_at < now() - (@RetentionDays || ' days')::INTERVAL
```

---

## Wire in InfrastructureExtensions.cs

```csharp
services.AddScoped<IAuditLogRepository, AuditLogRepository>();
services.AddHostedService<AuditLogCleanupService>();
```

---

## Testing After Deploy

1. `POST /api/projects` → check `platform.audit_log` has `action = 'project.create'`
2. `POST /api/auth/api-tokens` → check `action = 'token.create'`
3. `GET /api/platform/audit-log` → paginated list returned
4. Confirm `UPDATE`/`DELETE` denied in Supabase SQL editor
