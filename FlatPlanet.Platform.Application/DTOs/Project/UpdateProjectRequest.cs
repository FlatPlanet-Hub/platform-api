namespace FlatPlanet.Platform.Application.DTOs.Project;

public sealed class UpdateProjectRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? GitHubRepo { get; init; }
    public string? TechStack { get; init; }
}
