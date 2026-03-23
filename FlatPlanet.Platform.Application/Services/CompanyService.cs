using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Services;

public sealed class CompanyService(ICompanyRepository repo) : ICompanyService
{
    public async Task<CompanyDto> CreateAsync(CreateCompanyRequest request)
    {
        var existing = await repo.GetBySlugAsync(request.Slug);
        if (existing is not null)
            throw new InvalidOperationException($"Company slug '{request.Slug}' is already in use.");

        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Slug = request.Slug,
            CountryCode = request.CountryCode,
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await repo.CreateAsync(company);
        return ToDto(company);
    }

    public async Task<IEnumerable<CompanyDto>> ListAsync()
    {
        var companies = await repo.GetAllAsync();
        return companies.Select(ToDto);
    }

    public async Task<CompanyDto?> GetByIdAsync(Guid id)
    {
        var company = await repo.GetByIdAsync(id);
        return company is null ? null : ToDto(company);
    }

    public async Task<CompanyDto> UpdateAsync(Guid id, UpdateCompanyRequest request)
    {
        var company = await repo.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Company not found.");

        if (request.Name is not null) company.Name = request.Name;
        if (request.CountryCode is not null) company.CountryCode = request.CountryCode;
        company.UpdatedAt = DateTime.UtcNow;

        await repo.UpdateAsync(company);
        return ToDto(company);
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        var company = await repo.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Company not found.");
        await repo.UpdateStatusAsync(id, status);
    }

    private static CompanyDto ToDto(Company c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Slug = c.Slug,
        CountryCode = c.CountryCode,
        Status = c.Status,
        CreatedAt = c.CreatedAt
    };
}
