using FlatPlanet.Platform.Application.Common;
using FlatPlanet.Platform.Application.DTOs.Project;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ProjectService : IProjectService
{
    private readonly IProjectRepository _projectRepo;
    private readonly ISecurityPlatformService _securityPlatform;
    private readonly IGitHubRepoService _gitHubRepo;
    private readonly IDbProxyService _dbProxy;
    private readonly IAuditLogRepository _auditLog;

    public ProjectService(
        IProjectRepository projectRepo,
        ISecurityPlatformService securityPlatform,
        IGitHubRepoService gitHubRepo,
        IDbProxyService dbProxy,
        IAuditLogRepository auditLog)
    {
        _projectRepo      = projectRepo;
        _securityPlatform = securityPlatform;
        _gitHubRepo       = gitHubRepo;
        _dbProxy          = dbProxy;
        _auditLog         = auditLog;
    }

    public async Task<ProjectResponse> CreateProjectAsync(Guid userId, string actorEmail, Guid companyId, string baseUrl, CreateProjectRequest request, string? ipAddress)
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
            TechStack = request.TechStack,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _projectRepo.CreateAsync(project);

        var appId = await _securityPlatform.RegisterAppAsync(request.Name, appSlug, baseUrl, companyId);
        await _securityPlatform.SetupProjectRolesAsync(appId);
        await _securityPlatform.GrantRoleAsync(appId, userId, "owner");

        created.AppId = appId;
        created.AppSlug = appSlug;
        created.UpdatedAt = DateTime.UtcNow;
        await _projectRepo.UpdateAsync(created);

        _ = _gitHubRepo.SeedProjectFilesAsync(created);
        _ = _dbProxy.CreateSchemaAsync(schemaName);

        await _auditLog.LogAsync(userId, actorEmail, AuditAction.ProjectCreate,
            "project", created.Id, new { name = created.Name, appSlug }, ipAddress);

        return ToResponse(created, "owner");
    }

    public async Task<IEnumerable<ProjectResponse>> GetUserProjectsAsync(Guid userId)
    {
        var appAccess = await _securityPlatform.GetUserAppAccessAsync(userId);

        var canViewAll = appAccess.Any(a =>
            a.AppSlug.Equals("dashboard-hub", StringComparison.OrdinalIgnoreCase) &&
            a.Permissions.Contains("view_all_projects", StringComparer.OrdinalIgnoreCase));

        if (canViewAll)
        {
            var all = await _projectRepo.GetAllAsync();
            return all.Select(p =>
            {
                var entry = appAccess.FirstOrDefault(a => a.AppId == p.AppId);
                return ToResponse(p, entry?.RoleName ?? "admin");
            });
        }

        var appIds = appAccess.Select(a => a.AppId).ToList();
        if (appIds.Count == 0) return [];
        var projects = await _projectRepo.GetByAppIdsAsync(appIds);
        return projects.Select(p =>
        {
            var entry = appAccess.FirstOrDefault(a => a.AppId == p.AppId);
            return ToResponse(p, entry?.RoleName);
        });
    }

    public async Task<ProjectResponse> GetProjectAsync(Guid projectId, Guid userId)
    {
        var project = await GetOrThrowAsync(projectId);

        var appAccess = await _securityPlatform.GetUserAppAccessAsync(userId);

        var canViewAll = appAccess.Any(a =>
            a.AppSlug.Equals("dashboard-hub", StringComparison.OrdinalIgnoreCase) &&
            a.Permissions.Contains("view_all_projects", StringComparer.OrdinalIgnoreCase));

        if (!canViewAll && project.AppSlug is not null)
        {
            var allowed = await _securityPlatform.AuthorizeAsync(project.AppSlug, projectId.ToString(), "read");
            if (!allowed) throw new UnauthorizedAccessException("You do not have read access to this project.");
        }

        var entry = appAccess.FirstOrDefault(a => a.AppId == project.AppId);
        return ToResponse(project, entry?.RoleName ?? (canViewAll ? "admin" : null));
    }

    public async Task<ProjectResponse> UpdateProjectAsync(Guid projectId, Guid userId, UpdateProjectRequest request)
    {
        var project = await GetOrThrowAsync(projectId);
        if (project.AppSlug is not null)
        {
            var allowed = await _securityPlatform.AuthorizeAsync(project.AppSlug, projectId.ToString(), "manage_members");
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

    public async Task DeactivateProjectAsync(Guid projectId, Guid userId, string actorEmail, string? ipAddress)
    {
        var project = await GetOrThrowAsync(projectId);
        if (project.AppSlug is not null)
        {
            var allowed = await _securityPlatform.AuthorizeAsync(project.AppSlug, projectId.ToString(), "delete_project");
            if (!allowed) throw new UnauthorizedAccessException("You do not have permission to deactivate this project.");
        }

        project.IsActive = false;
        project.UpdatedAt = DateTime.UtcNow;
        await _projectRepo.UpdateAsync(project);

        await _auditLog.LogAsync(userId, actorEmail, AuditAction.ProjectDeactivate,
            "project", projectId, new { projectId, name = project.Name }, ipAddress);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Project> GetOrThrowAsync(Guid projectId) =>
        await _projectRepo.GetByIdAsync(projectId)
        ?? throw new KeyNotFoundException($"Project {projectId} not found.");

    private static string GenerateSlug(string name)
    {
        var slug = System.Text.RegularExpressions.Regex.Replace(
            name.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-").Trim('-');
        return slug[..Math.Min(100, slug.Length)];
    }

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
