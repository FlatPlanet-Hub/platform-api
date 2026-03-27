namespace FlatPlanet.Platform.Infrastructure.Configuration;

public sealed class SupabaseSettings
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 6543;
    public string Database { get; init; } = "postgres";
    public string AdminUser { get; init; } = string.Empty;
    public string AdminPassword { get; init; } = string.Empty;

    public string BuildConnectionString() =>
        $"Host={Host};Port={Port};Database={Database};Username={AdminUser};Password={AdminPassword};SSL Mode=Require;Trust Server Certificate=true;Keepalive=30;Connection Idle Lifetime=300;";
}
