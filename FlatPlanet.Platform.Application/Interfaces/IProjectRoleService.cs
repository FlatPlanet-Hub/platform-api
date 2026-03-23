using FlatPlanet.Platform.Application.DTOs.Project;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IProjectRoleService
{
    Task<IEnumerable<ProjectRoleResponse>> GetProjectRolesAsync(Guid projectId, Guid userId);
    Task<ProjectRoleResponse> CreateProjectRoleAsync(Guid projectId, Guid userId, CreateProjectRoleRequest request);
    Task UpdateProjectRoleAsync(Guid projectId, Guid roleId, Guid userId, UpdateProjectRoleRequest request);
    Task DeleteProjectRoleAsync(Guid projectId, Guid roleId, Guid userId);
}
