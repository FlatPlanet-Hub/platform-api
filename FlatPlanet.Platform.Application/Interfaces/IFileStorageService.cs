using FlatPlanet.Platform.Application.DTOs.Storage;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IFileStorageService
{
    Task<FileDto> UploadAsync(Stream fileStream, string fileName, string contentType, UploadFileRequest request, Guid uploadedBy);
    Task<FileUrlResponse> GetSasUrlAsync(Guid fileId, Guid requestedBy, Guid? appId = null);
    Task<IEnumerable<FileDto>> ListAsync(string businessCode, string? category, string[]? tags, Guid? appId = null, Guid? uploadedBy = null);
    Task DeleteAsync(Guid fileId, Guid deletedBy, Guid? appId = null);
}
