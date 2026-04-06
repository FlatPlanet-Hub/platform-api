namespace FlatPlanet.Platform.Application.DTOs.Project;

public sealed class CreateProjectRequest
{
    public string  Name        { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? TechStack   { get; init; }
    public string ProjectType { get; init; } = "fullstack";
    public bool   AuthEnabled { get; init; } = false;
    public GitHubRepoRequest? GitHub { get; init; }
}

public sealed class GitHubRepoRequest
{
    public bool    CreateRepo       { get; init; } = false;
    public string? RepoName         { get; init; }  // required when CreateRepo = true
    public string? ExistingRepoUrl  { get; init; }  // required when CreateRepo = false
}
