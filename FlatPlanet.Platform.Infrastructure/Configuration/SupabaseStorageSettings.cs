namespace FlatPlanet.Platform.Infrastructure.Configuration;

public sealed class SupabaseStorageSettings
{
    public string ServiceRoleKey { get; set; } = string.Empty;
    public string StorageUrl { get; set; } = string.Empty;        // e.g. https://<ref>.supabase.co/storage/v1
    public int SignedUrlExpirySeconds { get; set; } = 3600;
    public long MaxFileSizeBytes { get; set; } = 52428800;        // 50MB
}
