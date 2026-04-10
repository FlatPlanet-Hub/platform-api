using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IGitHubRepoService
{
    /// <summary>
    /// Creates a new GitHub repo under the configured org and returns its full name and URL.
    /// </summary>
    Task<(string RepoFullName, string RepoLink)> CreateRepoAsync(string repoName);

    /// <summary>
    /// Generates and pushes CLAUDE.md to the repo. Creates or updates the file.
    /// </summary>
    Task PushClaudeMdAsync(string repoFullName, string branch, string content);

    Task SeedProjectFilesAsync(Project project);
    Task SyncDataDictionaryAsync(Guid projectId, string schema);
    Task InviteCollaboratorAsync(string repo, string githubUsername, string permission);
    Task RemoveCollaboratorAsync(string repo, string githubUsername);
    Task SetRepoSecretAsync(string repo, string secretName, string secretValue);
    Task UpdateWorkflowAsync(string repo, string workflowContent);
}
