using FlatPlanet.Platform.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Platform.Infrastructure.Common;

public sealed class AuditLogCleanupService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<AuditLogCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope  = scopeFactory.CreateScope();
                var auditLog     = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
                var days         = config.GetValue<int>("AuditLog:RetentionDays", 1095);
                await auditLog.DeleteExpiredAsync(days);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AuditLogCleanupService failed during scheduled run");
            }

            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }
}
