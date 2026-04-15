namespace FlatPlanet.Platform.Application.Interfaces;

public interface IStorageBucketService
{
    /// <summary>
    /// Ensures a bucket exists for the project, creating it if needed.
    /// Idempotent. Returns bucket name, provisionedAt, and whether it already existed.
    /// </summary>
    Task<(string BucketName, DateTime ProvisionedAt, bool AlreadyExisted)> EnsureBucketExistsAsync(
        Guid projectId, string? appSlug);
}
