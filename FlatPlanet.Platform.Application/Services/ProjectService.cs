using FlatPlanet.Platform.Application.DTOs.Project;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ProjectService : IProjectService
{
    private readonly IProjectRepository _projectRepo;
    private readonly IDbProxyService _dbProxy;
    private readonly ISecurityPlatformService _securityPlatform;
    private readonly IGitHubRepoService _gitHubRepo;

    public ProjectService(
        IProjectRepository projectRepo,
        IDbProxyService dbProxy,
        ISecurityPlatformService securityPlatform,
        IGitHubRepoService gitHubRepo)
    {
        _projectRepo = projectRepo;
        _dbProxy = dbProxy;
        _securityPlatform = securityPlatform;
        _gitHubRepo = gitHubRepo;
    }

    public async Task<ProjectResponse> CreateProjectAsync(Guid userId, Guid companyId, string baseUrl, CreateProjectRequest request)
    {
        var shortId = Guid.NewGuid().ToString("N")[..8];
        var schemaName = $"project_{shortId}";
        var appSlug = GenerateSlug(request.Name);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            SchemaName = schemaName,
            OwnerId = userId,
            AppSlug = appSlug,
            TechStack = request.TechStack,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _projectRepo.CreateAsync(project);
        await _dbProxy.CreateSchemaAsync(schemaName);

        var appId = await _securityPlatform.RegisterAppAsync(request.Name, appSlug, baseUrl, companyId);
        created.AppId = appId;
        created.UpdatedAt = DateTime.UtcNow;
        await _projectRepo.UpdateAsync(created);

        await _securityPlatform.GrantRoleAsync(appId, userId, "owner");

        _ = Task.Run(async () =>
        {
            try { await _gitHubRepo.SeedProjectFilesAsync(created); }
            catch { /* fire-and-forget */ }
        });

        return ToResponse(created, "owner");
    }

    public async Task<IEnumerable<ProjectResponse>> GetUserProjectsAsync(Guid userId)
    {
        var appRoles = await _securityPlatform.GetUserAppRolesAsync(userId);
        var responses = new List<ProjectResponse>();

        foreach (var dto in appRoles)
        {
            var project = await _projectRepo.GetByAppSlugAsync(dto.AppSlug);
            if (project is null) continue;
            responses.Add(ToResponse(project, dto.RoleName));
        }

        return responses;
    }

    public async Task<ProjectResponse> GetProjectAsync(Guid projectId, Guid userId)
    {
        var project = await GetOrThrowAsync(projectId);
        if (project.AppSlug is not null)
        {
            var allowed = await _securityPlatform.AuthorizeAsync(project.AppSlug, projectId.ToString(), "read", userId);
            if (!allowed) throw new UnauthorizedAccessException("You do not have read access to this project.");
        }
        return ToResponse(project);
    }

    public async Task<ProjectResponse> UpdateProjectAsync(Guid projectId, Guid userId, UpdateProjectRequest request)
    {
        var project = await GetOrThrowAsync(projectId);
        if (project.AppSlug is not null)
        {
            var allowed = await _securityPlatform.AuthorizeAsync(project.AppSlug, projectId.ToString(), "manage_members", userId);
            if (!allowed) throw new UnauthorizedAccessException("You do not have permission to update this project.");
        }

        if (request.Name is not null) project.Name = request.Name;
        if (request.Description is not null) project.Description = request.Description;
        if (request.GitHubRepo is not null) project.GitHubRepo = request.GitHubRepo;
        if (request.TechStack is not null) project.TechStack = request.TechStack;
        project.UpdatedAt = DateTime.UtcNow;

        await _projectRepo.UpdateAsync(project);
        return ToResponse(project);
    }

    public async Task DeactivateProjectAsync(Guid projectId, Guid userId)
    {
        var project = await GetOrThrowAsync(projectId);
        if (project.AppSlug is not null)
        {
            var allowed = await _securityPlatform.AuthorizeAsync(project.AppSlug, projectId.ToString(), "delete_project", userId);
            if (!allowed) throw new UnauthorizedAccessException("You do not have permission to deactivate this project.");
        }

        project.IsActive = false;
        project.UpdatedAt = DateTime.UtcNow;
        await _projectRepo.UpdateAsync(project);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Project> GetOrThrowAsync(Guid projectId) =>
        await _projectRepo.GetByIdAsync(projectId)
        ?? throw new KeyNotFoundException($"Project {projectId} not found.");

    private static string GenerateSlug(string name) =>
        System.Text.RegularExpressions.Regex.Replace(
            name.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-").Trim('-');

    private static ProjectResponse ToResponse(Project p, string? roleName = null) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        SchemaName = p.SchemaName,
        OwnerId = p.OwnerId,
        AppSlug = p.AppSlug,
        GitHubRepo = p.GitHubRepo,
        TechStack = p.TechStack,
        IsActive = p.IsActive,
        CreatedAt = p.CreatedAt,
        RoleName = roleName
    };
}
