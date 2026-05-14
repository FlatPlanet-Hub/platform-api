using FlatPlanet.Platform.Application.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FlatPlanet.Platform.API.HealthChecks;

/// <summary>
/// Verifies the database is reachable by executing a lightweight SELECT 1.
/// Ensures /health returns Unhealthy before the slot swap receives live traffic
/// when the DB connection cannot be established.
/// </summary>
public sealed class DbHealthCheck(IDbConnectionFactory db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = await db.CreateConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = 5;
            await cmd.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException)
        {
            // Health check was cancelled — propagate so ASP.NET Core handles it correctly.
            throw;
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database unreachable.", ex);
        }
    }
}
