namespace FlatPlanet.Platform.Application.DTOs.Iam;

public sealed class CompanyDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public sealed class CreateCompanyRequest
{
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
}

public sealed class UpdateCompanyRequest
{
    public string? Name { get; init; }
    public string? CountryCode { get; init; }
}

public sealed class UpdateCompanyStatusRequest
{
    public string Status { get; init; } = string.Empty;
}
