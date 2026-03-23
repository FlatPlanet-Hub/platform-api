namespace FlatPlanet.Platform.Application.DTOs.Project;

public sealed class CreateProjectRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
