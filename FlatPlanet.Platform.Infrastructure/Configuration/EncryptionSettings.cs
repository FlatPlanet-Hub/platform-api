namespace FlatPlanet.Platform.Infrastructure.Configuration;

public sealed class EncryptionSettings
{
    public string Key { get; init; } = string.Empty; // 32-char AES-256 key
}
