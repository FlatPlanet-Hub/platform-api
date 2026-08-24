namespace FlatPlanet.Platform.Infrastructure.Configuration;

public sealed class SupabaseSettings
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 6543;
    public string Database { get; init; } = "postgres";
    public string AdminUser { get; init; } = string.Empty;
    public string AdminPassword { get; init; } = string.Empty;

    public string BuildConnectionString() =>
        $"Host={Host};Port={Port};Database={Database};Username={AdminUser};Password={AdminPassword};" +
        $"SSL Mode=Require;Trust Server Certificate=true;" +
        $"No Reset On Close=true;" +
        $"Minimum Pool Size=0;Maximum Pool Size=20;" +
        $"Max Auto Prepare=0;" +
        $"Command Timeout=30;" +
        // Connection acquisition timeout reduced from 30s → 10s.
        // Defense in depth against pool saturation: if all 20 pool slots are busy
        // (e.g. a runaway agent), new requests fail fast in 10s instead of holding
        // an HTTP thread for 30s. Combined with per-project rate limiting at 100/min,
        // makes pool exhaustion much harder. Legitimate slow operations still have
        // plenty of headroom (Command Timeout=30s for the query itself once a
        // connection is acquired).
        $"Timeout=10;";
}
