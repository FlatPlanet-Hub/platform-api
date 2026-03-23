namespace FlatPlanet.Platform.Application.DTOs.Iam;

public sealed class AppDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string? SchemaName { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public sealed class RegisterAppRequest
{
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
}

public sealed class UpdateAppRequest
{
    public string? Name { get; init; }
    public string? BaseUrl { get; init; }
}

public sealed class UpdateAppStatusRequest
{
    public string Status { get; init; } = string.Empty;
}
