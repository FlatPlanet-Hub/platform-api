using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using FlatPlanet.Platform.Application.DTOs.Storage;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using FlatPlanet.Platform.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Platform.Infrastructure.Storage;

public class AzureBlobStorageService : IFileStorageService
{
    private readonly BlobContainerClient _container;
    private readonly BlobServiceClient _serviceClient;
    private readonly StorageSettings _settings;
    private readonly IFileRepository _fileRepo;

    public AzureBlobStorageService(IOptions<StorageSettings> settings, IFileRepository fileRepo)
    {
        _settings = settings.Value;
        _fileRepo = fileRepo;

        var credential = new DefaultAzureCredential();
        var serviceUri = new Uri($"https://{_settings.AccountName}.blob.core.windows.net");
        _serviceClient = new BlobServiceClient(serviceUri, credential);
        _container = _serviceClient.GetBlobContainerClient(_settings.ContainerName);
    }

    public async Task<FileDto> UploadAsync(Stream fileStream, string fileName, string contentType, UploadFileRequest request, Guid uploadedBy)
    {
        if (fileStream.Length > _settings.MaxFileSizeBytes)
            throw new InvalidOperationException($"File exceeds maximum allowed size of {_settings.MaxFileSizeBytes / 1024 / 1024}MB.");

        var fileId = Guid.NewGuid();
        var ext = Path.GetExtension(fileName);
        var scopeSegment = request.AppId.HasValue ? request.AppId.Value.ToString() : "unscoped";
        var blobName = $"{request.BusinessCode}/{scopeSegment}/{request.Category}/{fileId}{ext}";

        var blobClient = _container.GetBlobClient(blobName);
        await blobClient.UploadAsync(fileStream, new BlobHttpHeaders { ContentType = contentType });

        var file = new PlatformFile
        {
            Id = fileId,
            AppId = request.AppId,
            BusinessCode = request.BusinessCode,
            Category = request.Category,
            OriginalName = fileName,
            BlobName = blobName,
            ContentType = contentType,
            FileSizeBytes = fileStream.Length,
            UploadedBy = uploadedBy,
            Tags = request.Tags,
            CreatedAt = DateTime.UtcNow
        };

        await _fileRepo.InsertAsync(file);

        var sasExpiry = DateTime.UtcNow.AddMinutes(_settings.SasExpiryMinutes);
        var sasUrl = await GenerateSasUrlAsync(blobClient, sasExpiry);

        return ToDto(file, sasUrl, sasExpiry);
    }

    public async Task<FileUrlResponse> GetSasUrlAsync(Guid fileId, Guid requestedBy)
    {
        var file = await _fileRepo.GetByIdAsync(fileId)
            ?? throw new KeyNotFoundException($"File {fileId} not found.");

        var blobClient = _container.GetBlobClient(file.BlobName);
        var expiry = DateTime.UtcNow.AddMinutes(_settings.SasExpiryMinutes);
        var sasUrl = await GenerateSasUrlAsync(blobClient, expiry);

        return new FileUrlResponse(sasUrl, expiry);
    }

    public async Task<IEnumerable<FileDto>> ListAsync(string businessCode, string? category, string[]? tags, Guid? appId = null)
    {
        var files = await _fileRepo.ListAsync(businessCode, category, tags, appId);
        var expiry = DateTime.UtcNow.AddMinutes(_settings.SasExpiryMinutes);

        var dtos = new List<FileDto>();
        foreach (var f in files)
        {
            var blobClient = _container.GetBlobClient(f.BlobName);
            var sasUrl = await GenerateSasUrlAsync(blobClient, expiry);
            dtos.Add(ToDto(f, sasUrl, expiry));
        }
        return dtos;
    }

    public async Task DeleteAsync(Guid fileId, Guid deletedBy)
    {
        var file = await _fileRepo.GetByIdAsync(fileId)
            ?? throw new KeyNotFoundException($"File {fileId} not found.");

        var blobClient = _container.GetBlobClient(file.BlobName);
        await blobClient.DeleteIfExistsAsync();
        await _fileRepo.SoftDeleteAsync(fileId, DateTime.UtcNow);
    }

    private async Task<string> GenerateSasUrlAsync(BlobClient blobClient, DateTime expiry)
    {
        // With Managed Identity, use user delegation key for SAS
        var delegationKey = await _serviceClient.GetUserDelegationKeyAsync(DateTimeOffset.UtcNow, expiry);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            ExpiresOn = expiry
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sas = sasBuilder.ToSasQueryParameters(delegationKey, blobClient.AccountName);
        return $"{blobClient.Uri}?{sas}";
    }

    private static FileDto ToDto(PlatformFile file, string sasUrl, DateTime sasExpiry) =>
        new(file.Id, file.BusinessCode, file.Category, file.OriginalName,
            file.ContentType, file.FileSizeBytes, file.Tags, sasUrl, sasExpiry, file.CreatedAt);
}
