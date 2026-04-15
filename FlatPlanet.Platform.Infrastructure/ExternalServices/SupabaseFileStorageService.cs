using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FlatPlanet.Platform.Application.DTOs.Storage;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using FlatPlanet.Platform.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class SupabaseFileStorageService : IFileStorageService
{
    private readonly HttpClient _http;
    private readonly SupabaseStorageSettings _settings;
    private readonly IFileRepository _fileRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly IStorageBucketService _bucketService;
    private readonly ILogger<SupabaseFileStorageService> _logger;

    public SupabaseFileStorageService(
        IHttpClientFactory httpClientFactory,
        IOptions<SupabaseStorageSettings> settings,
        IFileRepository fileRepo,
        IProjectRepository projectRepo,
        IStorageBucketService bucketService,
        ILogger<SupabaseFileStorageService> logger)
    {
        _http = httpClientFactory.CreateClient("SupabaseStorage");
        _settings = settings.Value;
        _fileRepo = fileRepo;
        _projectRepo = projectRepo;
        _bucketService = bucketService;
        _logger = logger;
    }

    public async Task<FileDto> UploadAsync(
        Stream fileStream, string fileName, string contentType,
        long fileSize, UploadFileRequest request, Guid uploadedBy)
    {
        if (fileSize > _settings.MaxFileSizeBytes)
            throw new InvalidOperationException(
                $"File exceeds maximum allowed size of {_settings.MaxFileSizeBytes / 1024 / 1024}MB.");

        if (!request.AppId.HasValue)
            throw new UnauthorizedAccessException(
                "File uploads require a project-scoped token.");

        var bucketName = await ResolveBucketAsync(request.AppId.Value);

        var fileId = Guid.NewGuid();
        var ext = Path.GetExtension(fileName);
        var objectPath = $"{request.Category}/{fileId}{ext}";

        using var content = new StreamContent(fileStream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        var response = await _http.PostAsync($"object/{bucketName}/{objectPath}", content);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Supabase Storage upload failed: HTTP {(int)response.StatusCode} — {body}");
        }

        var file = new PlatformFile
        {
            Id            = fileId,
            AppId         = request.AppId,
            BusinessCode  = request.BusinessCode,
            Category      = request.Category,
            OriginalName  = fileName,
            BlobName      = objectPath,     // relative path within bucket
            ContentType   = contentType,
            FileSizeBytes = fileSize,
            UploadedBy    = uploadedBy,
            Tags          = request.Tags,
            CreatedAt     = DateTime.UtcNow
        };

        await _fileRepo.InsertAsync(file);

        var (signedUrl, expiry) = await GenerateSignedUrlAsync(bucketName, objectPath);
        return ToDto(file, signedUrl, expiry);
    }

    public async Task<FileUrlResponse> GetSasUrlAsync(Guid fileId, Guid requestedBy, Guid? appId = null)
    {
        var file = await _fileRepo.GetByIdAsync(fileId, appId)
            ?? throw new KeyNotFoundException($"File {fileId} not found.");

        if (!appId.HasValue && file.AppId.HasValue)
            throw new UnauthorizedAccessException("You do not have access to this file.");

        if (!appId.HasValue && file.UploadedBy != requestedBy)
            throw new UnauthorizedAccessException("You can only access files you uploaded.");

        var bucketName = await GetBucketForFileAsync(file);
        var (signedUrl, expiry) = await GenerateSignedUrlAsync(bucketName, file.BlobName);
        return new FileUrlResponse(signedUrl, expiry);
    }

    public async Task<IEnumerable<FileDto>> ListAsync(
        string businessCode, string? category, string[]? tags,
        Guid? appId = null, Guid? uploadedBy = null)
    {
        var effectiveUploadedBy = appId.HasValue ? null : uploadedBy;
        var files = (await _fileRepo.ListAsync(businessCode, category, tags, appId, effectiveUploadedBy)).ToList();

        var dtos = new List<FileDto>();
        var expiry = DateTime.UtcNow.AddSeconds(_settings.SignedUrlExpirySeconds);

        // Group by AppId to resolve bucket once per project
        var byApp = files.GroupBy(f => f.AppId);
        foreach (var group in byApp)
        {
            string? bucketName = null;
            if (group.Key.HasValue)
            {
                var project = await _projectRepo.GetByAppIdAsync(group.Key.Value);
                bucketName = project?.BucketName;
            }

            foreach (var f in group)
            {
                if (bucketName is null)
                {
                    _logger.LogWarning("Skipping file {FileId}: project has no storage bucket provisioned.", f.Id);
                    continue;
                }

                var (signedUrl, fileExpiry) = await GenerateSignedUrlAsync(bucketName, f.BlobName);
                dtos.Add(ToDto(f, signedUrl, fileExpiry));
            }
        }

        return dtos;
    }

    public async Task DeleteAsync(Guid fileId, Guid deletedBy, Guid? appId = null)
    {
        var file = await _fileRepo.GetByIdAsync(fileId, appId)
            ?? throw new KeyNotFoundException($"File {fileId} not found.");

        if (!appId.HasValue && file.AppId.HasValue)
            throw new UnauthorizedAccessException("You do not have access to this file.");

        if (!appId.HasValue && file.UploadedBy != deletedBy)
            throw new UnauthorizedAccessException("You can only delete files you uploaded.");

        // Soft-delete DB record first
        await _fileRepo.SoftDeleteAsync(fileId, DateTime.UtcNow);

        // Then delete from Supabase (non-fatal if blob delete fails)
        try
        {
            var bucketName = await GetBucketForFileAsync(file);
            var deletePayload = new { prefixes = new[] { file.BlobName } };
            await _http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"object/{bucketName}")
            {
                Content = JsonContent.Create(deletePayload)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete blob for file {FileId} from Supabase Storage. DB record is soft-deleted.", fileId);
        }
    }

    private async Task<string> ResolveBucketAsync(Guid appId)
    {
        var project = await _projectRepo.GetByAppIdAsync(appId)
            ?? throw new KeyNotFoundException($"No project found for app_id {appId}.");

        if (project.BucketName is not null)
            return project.BucketName;

        // Lazy creation — safety net for projects that skipped explicit provisioning
        var (bucketName, _, _) = await _bucketService.EnsureBucketExistsAsync(project.Id, project.AppSlug);
        await _projectRepo.UpdateBucketNameAsync(project.Id, bucketName);
        return bucketName;
    }

    private async Task<string> GetBucketForFileAsync(PlatformFile file)
    {
        if (!file.AppId.HasValue)
            throw new InvalidOperationException("Cannot resolve storage bucket for unscoped file.");

        var project = await _projectRepo.GetByAppIdAsync(file.AppId.Value)
            ?? throw new KeyNotFoundException($"No project found for app_id {file.AppId.Value}.");

        if (project.BucketName is null)
            throw new InvalidOperationException(
                $"Project has no storage bucket provisioned. Call POST /api/v1/projects/{{id}}/storage/provision first.");

        return project.BucketName;
    }

    private async Task<(string Url, DateTime Expiry)> GenerateSignedUrlAsync(string bucketName, string objectPath)
    {
        var payload = new { expiresIn = _settings.SignedUrlExpirySeconds };
        var response = await _http.PostAsJsonAsync($"object/sign/{bucketName}/{objectPath}", payload);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to generate signed URL for '{objectPath}': HTTP {(int)response.StatusCode}");

        var result = await response.Content.ReadFromJsonAsync<SupabaseSignedUrlResponse>()
            ?? throw new InvalidOperationException("Empty response from Supabase signed URL endpoint.");

        // result.SignedUrl is a full URL or a path — ensure it's absolute
        var fullUrl = result.SignedUrl.StartsWith("http")
            ? result.SignedUrl
            : _settings.StorageUrl.TrimEnd('/') + result.SignedUrl;

        return (fullUrl, DateTime.UtcNow.AddSeconds(_settings.SignedUrlExpirySeconds));
    }

    private static FileDto ToDto(PlatformFile file, string signedUrl, DateTime expiry) =>
        new(file.Id, file.AppId, file.BusinessCode, file.Category, file.OriginalName,
            file.ContentType, file.FileSizeBytes, file.Tags, signedUrl, expiry, file.CreatedAt);

    // Supabase returns { "signedURL": "..." } — capital URL
    private sealed record SupabaseSignedUrlResponse(
        [property: JsonPropertyName("signedURL")] string SignedUrl);
}
