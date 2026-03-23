using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IAppRepository
{
    Task<App> CreateAsync(App app);
    Task<App?> GetByIdAsync(Guid id);
    Task<App?> GetBySlugAsync(string slug);
    Task<IEnumerable<App>> GetAllAsync();
    Task<IEnumerable<App>> GetByCompanyIdAsync(Guid companyId);
    Task<IEnumerable<App>> GetByUserIdAsync(Guid userId);
    Task UpdateAsync(App app);
    Task UpdateStatusAsync(Guid id, string status);
}
