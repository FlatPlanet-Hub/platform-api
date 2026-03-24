namespace FlatPlanet.Platform.Application.DTOs.Iam;

public sealed class ResourceDto
{
    public Guid Id { get; init; }
    public Guid AppId { get; init; }
    public Guid ResourceTypeId { get; init; }
    public string ResourceTypeName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Identifier { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public sealed class CreateResourceRequest
{
    public Guid ResourceTypeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Identifier { get; init; } = string.Empty;
}

public sealed class UpdateResourceRequest
{
    public string? Name { get; init; }
    public string? Identifier { get; init; }
    public string? Status { get; init; }
}

public sealed class ResourceTypeDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
