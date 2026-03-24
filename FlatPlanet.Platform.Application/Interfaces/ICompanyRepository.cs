using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface ICompanyRepository
{
    Task<Company> CreateAsync(Company company);
    Task<Company?> GetByIdAsync(Guid id);
    Task<Company?> GetBySlugAsync(string slug);
    Task<IEnumerable<Company>> GetAllAsync();
    Task UpdateAsync(Company company);
    Task UpdateStatusAsync(Guid id, string status);
}
