using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class CompanyRepository(IDbConnectionFactory connectionFactory) : ICompanyRepository
{
    public async Task<Company> CreateAsync(Company company)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync("""
            INSERT INTO platform.companies (id, name, slug, country_code, status, created_at, updated_at)
            VALUES (@Id, @Name, @Slug, @CountryCode, @Status, @CreatedAt, @UpdatedAt)
            """, company);
        return company;
    }

    public async Task<Company?> GetByIdAsync(Guid id)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Company>(
            "SELECT * FROM platform.companies WHERE id = @Id", new { Id = id });
    }

    public async Task<Company?> GetBySlugAsync(string slug)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Company>(
            "SELECT * FROM platform.companies WHERE slug = @Slug", new { Slug = slug });
    }

    public async Task<IEnumerable<Company>> GetAllAsync()
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<Company>(
            "SELECT * FROM platform.companies ORDER BY name");
    }

    public async Task UpdateAsync(Company company)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync("""
            UPDATE platform.companies
            SET name = @Name, country_code = @CountryCode, updated_at = now()
            WHERE id = @Id
            """, company);
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.companies SET status = @Status, updated_at = now() WHERE id = @Id",
            new { Id = id, Status = status });
    }
}
