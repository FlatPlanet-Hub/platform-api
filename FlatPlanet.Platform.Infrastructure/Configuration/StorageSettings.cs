namespace FlatPlanet.Platform.Infrastructure.Configuration;

public class StorageSettings
{
    public string AccountName { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "flatplanet-assets";
    public int SasExpiryMinutes { get; set; } = 60;
    public long MaxFileSizeBytes { get; set; } = 52428800; // 50MB
}
