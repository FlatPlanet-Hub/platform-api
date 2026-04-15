using FlatPlanet.Platform.Application.DTOs.Project;
using FlatPlanet.Platform.Application.DTOs.Storage;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IProjectService
{
    Task<ProjectResponse> CreateProjectAsync(Guid userId, string actorEmail, Guid companyId, string baseUrl, CreateProjectRequest request);
    Task<IEnumerable<ProjectResponse>> GetUserProjectsAsync(Guid userId);
    Task<ProjectResponse> GetProjectAsync(Guid projectId, Guid userId);
    Task<ProjectResponse> UpdateProjectAsync(Guid projectId, Guid userId, UpdateProjectRequest request);
    Task DeactivateProjectAsync(Guid projectId, Guid userId);
    Task<(int pushed, int skipped, List<string> failures)> SyncAllClaudeMdAsync(Guid actorId, string actorEmail, string baseUrl);
    Task<StorageProvisionResponse> ProvisionStorageAsync(Guid projectId, Guid userId);
}
