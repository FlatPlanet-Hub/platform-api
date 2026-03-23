using FlatPlanet.Platform.Application.DTOs.Project;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ProjectRoleService : IProjectRoleService
{
    private readonly IProjectRoleRepository _roleRepo;
    private readonly IProjectMemberRepository _memberRepo;
    private readonly IAuditService _audit;

    public ProjectRoleService(
        IProjectRoleRepository roleRepo,
        IProjectMemberRepository memberRepo,
        IAuditService audit)
    {
        _roleRepo = roleRepo;
        _memberRepo = memberRepo;
        _audit = audit;
    }

    public async Task<IEnumerable<ProjectRoleResponse>> GetProjectRolesAsync(Guid projectId, Guid userId)
    {
        await RequireMembershipAsync(projectId, userId);
        var roles = await _roleRepo.GetByProjectIdAsync(projectId);
        return roles.Select(r => new ProjectRoleResponse
        {
            Id = r.Id,
            Name = r.Name,
            Permissions = r.Permissions,
            IsDefault = r.IsDefault
        });
    }

    public async Task<ProjectRoleResponse> CreateProjectRoleAsync(Guid projectId, Guid userId, CreateProjectRoleRequest request)
    {
        await RequirePermissionAsync(projectId, userId, "manage_members");

        var existing = await _roleRepo.GetByNameAsync(projectId, request.Name);
        if (existing is not null)
            throw new InvalidOperationException($"Role '{request.Name}' already exists.");

        var role = new ProjectRole
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = request.Name,
            Permissions = request.Permissions,
            IsDefault = false,
            CreatedAt = DateTime.UtcNow
        };

        var created = await _roleRepo.CreateAsync(role);
        return new ProjectRoleResponse { Id = created.Id, Name = created.Name, Permissions = created.Permissions, IsDefault = created.IsDefault };
    }

    public async Task UpdateProjectRoleAsync(Guid projectId, Guid roleId, Guid userId, UpdateProjectRoleRequest request)
    {
        await RequirePermissionAsync(projectId, userId, "manage_members");
        var role = await _roleRepo.GetByIdAsync(projectId, roleId)
            ?? throw new KeyNotFoundException("Role not found.");

        if (request.Permissions is not null) role.Permissions = request.Permissions;
        await _roleRepo.UpdateAsync(role);
    }

    public async Task DeleteProjectRoleAsync(Guid projectId, Guid roleId, Guid userId)
    {
        await RequirePermissionAsync(projectId, userId, "manage_members");
        var role = await _roleRepo.GetByIdAsync(projectId, roleId)
            ?? throw new KeyNotFoundException("Role not found.");
        if (role.IsDefault)
            throw new InvalidOperationException("Default roles cannot be deleted.");
        await _roleRepo.DeleteAsync(roleId);
    }

    private async Task RequireMembershipAsync(Guid projectId, Guid userId)
    {
        var member = await _memberRepo.GetAsync(projectId, userId);
        if (member is null)
            throw new UnauthorizedAccessException("You are not a member of this project.");
    }

    private async Task RequirePermissionAsync(Guid projectId, Guid userId, string permission)
    {
        var member = await _memberRepo.GetAsync(projectId, userId)
            ?? throw new UnauthorizedAccessException("You are not a member of this project.");
        var role = await _roleRepo.GetByIdAsync(projectId, member.ProjectRoleId)
            ?? throw new UnauthorizedAccessException("Your project role could not be found.");
        if (!role.Permissions.Contains(permission))
            throw new UnauthorizedAccessException($"You do not have '{permission}' permission on this project.");
    }
}
