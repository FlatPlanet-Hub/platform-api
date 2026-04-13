namespace FlatPlanet.Platform.Application.DTOs.Azure;

public sealed record SyncGitHubActionsResponse(
    string AppServiceName,
    string RepoFullName,
    string Message);
