namespace FlatPlanet.Platform.Application.DTOs.Admin;

public sealed class PermissionDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Category { get; init; } = string.Empty;
}
