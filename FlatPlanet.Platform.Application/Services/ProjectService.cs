using FlatPlanet.Platform.Application.DTOs.Project;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ProjectService : IProjectService
{
    private readonly IProjectRepository _projectRepo;
    private readonly IProjectRoleRepository _roleRepo;
    private readonly IProjectMemberRepository _memberRepo;
    private readonly IUserRepository _userRepo;
    private readonly IDbProxyService _dbProxy;
    private readonly IAuditService _audit;

    public ProjectService(
        IProjectRepository projectRepo,
        IProjectRoleRepository roleRepo,
        IProjectMemberRepository memberRepo,
        IUserRepository userRepo,
        IDbProxyService dbProxy,
        IAuditService audit)
    {
        _projectRepo = projectRepo;
        _roleRepo = roleRepo;
        _memberRepo = memberRepo;
        _userRepo = userRepo;
        _dbProxy = dbProxy;
        _audit = audit;
    }

    public async Task<ProjectResponse> CreateProjectAsync(Guid userId, CreateProjectRequest request)
    {
        var shortId = Guid.NewGuid().ToString("N")[..8];
        var schemaName = $"project_{shortId}";

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            SchemaName = schemaName,
            OwnerId = userId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _projectRepo.CreateAsync(project);
        await _dbProxy.CreateSchemaAsync(schemaName);

        var defaultRoles = BuildDefaultRoles(created.Id);
        foreach (var role in defaultRoles)
            await _roleRepo.CreateAsync(role);

        var ownerRole = defaultRoles.First(r => r.Name == "owner");
        await _memberRepo.AddAsync(new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = created.Id,
            UserId = userId,
            ProjectRoleId = ownerRole.Id,
            Status = "active",
            JoinedAt = DateTime.UtcNow
        });

        await _audit.LogAsync(userId, created.Id, "project.created", "project",
            new { projectName = request.Name, schemaName });

        return ToResponse(created);
    }

    public async Task<IEnumerable<ProjectResponse>> GetUserProjectsAsync(Guid userId)
    {
        var projects = await _projectRepo.GetByUserIdAsync(userId);
        return projects.Select(ToResponse);
    }

    public async Task<ProjectResponse> GetProjectAsync(Guid projectId, Guid userId)
    {
        var project = await GetOrThrowAsync(projectId);
        await RequireMembershipAsync(projectId, userId);
        return ToResponse(project);
    }

    public async Task<ProjectResponse> UpdateProjectAsync(Guid projectId, Guid userId, UpdateProjectRequest request)
    {
        var project = await GetOrThrowAsync(projectId);
        await RequirePermissionAsync(projectId, userId, "manage_members");

        if (request.Name is not null) project.Name = request.Name;
        if (request.Description is not null) project.Description = request.Description;
        if (request.GitHubRepo is not null) project.GitHubRepo = request.GitHubRepo;
        project.UpdatedAt = DateTime.UtcNow;

        await _projectRepo.UpdateAsync(project);
        await _audit.LogAsync(userId, projectId, "project.updated", "project");
        return ToResponse(project);
    }

    public async Task DeactivateProjectAsync(Guid projectId, Guid userId)
    {
        var project = await GetOrThrowAsync(projectId);
        await RequirePermissionAsync(projectId, userId, "delete_project");

        project.IsActive = false;
        project.UpdatedAt = DateTime.UtcNow;
        await _projectRepo.UpdateAsync(project);
        await _audit.LogAsync(userId, projectId, "project.deactivated", "project");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Project> GetOrThrowAsync(Guid projectId) =>
        await _projectRepo.GetByIdAsync(projectId)
        ?? throw new KeyNotFoundException($"Project {projectId} not found.");

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

    private static ProjectResponse ToResponse(Project p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        SchemaName = p.SchemaName,
        OwnerId = p.OwnerId,
        GitHubRepo = p.GitHubRepo,
        IsActive = p.IsActive,
        CreatedAt = p.CreatedAt
    };

    private static List<ProjectRole> BuildDefaultRoles(Guid projectId)
    {
        var now = DateTime.UtcNow;
        return
        [
            new ProjectRole { Id = Guid.NewGuid(), ProjectId = projectId, Name = "owner",     Permissions = ["read", "write", "ddl", "manage_members", "delete_project"], IsDefault = true, CreatedAt = now },
            new ProjectRole { Id = Guid.NewGuid(), ProjectId = projectId, Name = "developer", Permissions = ["read", "write", "ddl"], IsDefault = true, CreatedAt = now },
            new ProjectRole { Id = Guid.NewGuid(), ProjectId = projectId, Name = "viewer",    Permissions = ["read"], IsDefault = true, CreatedAt = now }
        ];
    }
}
