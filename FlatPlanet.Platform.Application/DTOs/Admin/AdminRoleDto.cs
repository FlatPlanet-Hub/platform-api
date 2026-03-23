namespace FlatPlanet.Platform.Application.DTOs.Admin;

public sealed class AdminRoleDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string[] Permissions { get; init; } = [];
    public bool IsSystem { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class CreateCustomRoleRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IEnumerable<string> Permissions { get; init; } = [];
}

public sealed class UpdateCustomRoleRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public IEnumerable<string>? Permissions { get; init; }
}
