using FlatPlanet.Platform.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FlatPlanet.Platform.Infrastructure.Services;

public sealed class AuditLogCleanupService : BackgroundService
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
            using var scope  = _scopeFactory.CreateScope();
            var auditLog     = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            var days         = _config.GetValue<int>("AuditLog:RetentionDays", 1095);
            await auditLog.DeleteExpiredAsync(days);
            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }
}
