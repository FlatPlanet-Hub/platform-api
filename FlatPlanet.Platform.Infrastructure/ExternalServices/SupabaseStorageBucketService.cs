using System.Net.Http.Json;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class SupabaseStorageBucketService : IStorageBucketService
{
    private readonly HttpClient _http;
    private readonly ILogger<SupabaseStorageBucketService> _logger;

    public SupabaseStorageBucketService(
        IHttpClientFactory httpClientFactory,
        ILogger<SupabaseStorageBucketService> logger)
    {
        _http = httpClientFactory.CreateClient("SupabaseStorage");
        _logger = logger;
    }

    public static string BuildBucketName(Guid projectId, string? appSlug)
    {
        if (string.IsNullOrWhiteSpace(appSlug))
            return $"proj-{projectId:N}";  // fallback: proj-{uuid without hyphens}

        // Lowercase, replace non-alphanumeric runs with hyphens, strip edges, max 40 chars for slug part
        var sanitised = System.Text.RegularExpressions.Regex
            .Replace(appSlug.ToLowerInvariant(), @"[^a-z0-9]+", "-")
            .Trim('-');
        sanitised = sanitised[..Math.Min(40, sanitised.Length)];
        return $"proj-{sanitised}";
    }

    public async Task<(string BucketName, DateTime ProvisionedAt, bool AlreadyExisted)> EnsureBucketExistsAsync(
        Guid projectId, string? appSlug)
    {
        var bucketName = BuildBucketName(projectId, appSlug);

        // Check if bucket already exists
        var check = await _http.GetAsync($"bucket/{bucketName}");
        if (check.IsSuccessStatusCode)
        {
            var existing = await check.Content.ReadFromJsonAsync<SupabaseBucketResponse>();
            _logger.LogInformation("Bucket {BucketName} already exists for project {ProjectId}", bucketName, projectId);
            return (bucketName, existing?.CreatedAt ?? DateTime.UtcNow, true);
        }

        // Create the bucket (private, not public)
        var payload = new { id = bucketName, name = bucketName, @public = false };
        var create = await _http.PostAsJsonAsync("bucket", payload);

        if (create.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Race condition — bucket was just created by another request
            var existing = await _http.GetAsync($"bucket/{bucketName}");
            var bucket = await existing.Content.ReadFromJsonAsync<SupabaseBucketResponse>();
            return (bucketName, bucket?.CreatedAt ?? DateTime.UtcNow, true);
        }

        if (!create.IsSuccessStatusCode)
        {
            var body = await create.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Failed to create Supabase bucket '{bucketName}': HTTP {(int)create.StatusCode} — {body}");
        }

        _logger.LogInformation("Created Supabase bucket {BucketName} for project {ProjectId}", bucketName, projectId);
        return (bucketName, DateTime.UtcNow, false);
    }

    private sealed record SupabaseBucketResponse(
        string Id,
        string Name,
        [property: System.Text.Json.Serialization.JsonPropertyName("created_at")] DateTime CreatedAt);
}
