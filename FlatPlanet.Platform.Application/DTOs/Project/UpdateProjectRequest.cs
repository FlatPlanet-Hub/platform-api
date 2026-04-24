namespace FlatPlanet.Platform.Application.DTOs.Project;

public sealed class UpdateProjectRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? GitHubRepo { get; init; }
    public string? TechStack { get; init; }
    public string? ProjectType { get; init; }
    public bool?   AuthEnabled { get; init; }
    public string? NetlifySiteId { get; init; }
}
