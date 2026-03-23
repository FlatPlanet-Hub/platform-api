using FlatPlanet.Platform.Application.DTOs.Iam;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface ICompanyService
{
    Task<CompanyDto> CreateAsync(CreateCompanyRequest request);
    Task<IEnumerable<CompanyDto>> ListAsync();
    Task<CompanyDto?> GetByIdAsync(Guid id);
    Task<CompanyDto> UpdateAsync(Guid id, UpdateCompanyRequest request);
    Task UpdateStatusAsync(Guid id, string status);
}
