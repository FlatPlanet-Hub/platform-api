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
        $"Keepalive=30;" +
        $"Minimum Pool Size=1;Maximum Pool Size=20;" +
        $"Max Auto Prepare=0;" +
        $"Command Timeout=30;" +
        $"Timeout=30;";
}
