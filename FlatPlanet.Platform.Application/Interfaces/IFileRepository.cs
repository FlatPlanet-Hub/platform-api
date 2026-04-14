using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IFileRepository
{
    Task<PlatformFile?> GetByIdAsync(Guid id, Guid? appId = null);
    Task<IEnumerable<PlatformFile>> ListAsync(string businessCode, string? category, string[]? tags, Guid? appId = null, Guid? uploadedBy = null);
    Task<Guid> InsertAsync(PlatformFile file);
    Task SoftDeleteAsync(Guid id, DateTime deletedAt);
}
