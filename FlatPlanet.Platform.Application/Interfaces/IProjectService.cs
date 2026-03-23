using FlatPlanet.Platform.Application.DTOs.Project;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IProjectService
{
    Task<ProjectResponse> CreateProjectAsync(Guid userId, CreateProjectRequest request);
    Task<IEnumerable<ProjectResponse>> GetUserProjectsAsync(Guid userId);
    Task<ProjectResponse> GetProjectAsync(Guid projectId, Guid userId);
    Task<ProjectResponse> UpdateProjectAsync(Guid projectId, Guid userId, UpdateProjectRequest request);
    Task DeactivateProjectAsync(Guid projectId, Guid userId);
}
