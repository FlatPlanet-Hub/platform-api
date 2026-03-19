namespace SupabaseProxy.Infrastructure.Configuration;

public sealed class JwtSettings
{
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public int ExpiryMinutes { get; init; } = 60;
}
