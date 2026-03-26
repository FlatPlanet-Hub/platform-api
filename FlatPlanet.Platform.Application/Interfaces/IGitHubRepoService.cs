using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IGitHubRepoService
{
    Task SeedProjectFilesAsync(Project project);
    Task SyncDataDictionaryAsync(Guid projectId, string schema);
    Task InviteCollaboratorAsync(string repo, string githubUsername, string permission);
    Task RemoveCollaboratorAsync(string repo, string githubUsername);
}
