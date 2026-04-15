using FlatPlanet.Platform.Application.DTOs.Project;
using FlatPlanet.Platform.Application.DTOs.Storage;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Services;

public sealed class ProjectService : IProjectService
{
    private readonly IProjectRepository _projectRepo;
    private readonly ISecurityPlatformService _securityPlatform;
    private readonly IGitHubRepoService _gitHubRepo;
    private readonly IDbProxyService _dbProxy;
    private readonly IClaudeConfigService _claudeConfig;
    private readonly IStorageBucketService _bucketService;

    public ProjectService(
        IProjectRepository projectRepo,
        ISecurityPlatformService securityPlatform,
        IGitHubRepoService gitHubRepo,
        IDbProxyService dbProxy,
        IClaudeConfigService claudeConfig,
        IStorageBucketService bucketService)
    {
        _projectRepo = projectRepo;
        _securityPlatform = securityPlatform;
        _gitHubRepo = gitHubRepo;
        _dbProxy = dbProxy;
        _claudeConfig = claudeConfig;
        _bucketService = bucketService;
    }

    public async Task<ProjectResponse> CreateProjectAsync(
        Guid userId, string actorEmail, Guid companyId, string baseUrl, CreateProjectRequest request)
    {
        var shortId    = Guid.NewGuid().ToString("N")[..8];
        var schemaName = $"project_{shortId}";
        var appSlug    = GenerateSlug(request.Name);

        // 1. Resolve GitHub repo info
        string? repoFullName = null;
        string? repoName     = null;
        string? repoLink     = null;
        const string branch  = "main";

        if (request.GitHub is not null)
        {
            if (request.GitHub.CreateRepo)
            {
                if (string.IsNullOrWhiteSpace(request.GitHub.RepoName))
                    throw new ArgumentException("RepoName is required when CreateRepo is true.");

                (repoFullName, repoLink) = await _gitHubRepo.CreateRepoAsync(request.GitHub.RepoName);
                repoName = request.GitHub.RepoName;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.GitHub.ExistingRepoUrl))
                    throw new ArgumentException("ExistingRepoUrl is required when CreateRepo is false.");

                repoLink     = request.GitHub.ExistingRepoUrl.TrimEnd('/');
                var uri      = new Uri(repoLink);
                repoFullName = uri.AbsolutePath.TrimStart('/');
                repoName     = repoFullName.Split('/').Last();
            }
        }

        if (!ValidProjectTypes.Contains(request.ProjectType.ToLowerInvariant()))
            throw new ArgumentException($"Invalid project_type '{request.ProjectType}'. Must be one of: frontend, backend, database, fullstack.");

        var projectType = request.ProjectType.ToLowerInvariant();

        // 2. Register with SP first — if this fails, nothing is persisted
        var appId = await _securityPlatform.RegisterAppAsync(request.Name, appSlug, baseUrl, companyId);
        await _securityPlatform.SetupProjectRolesAsync(appId);
        await _securityPlatform.GrantRoleAsync(appId, userId, "owner");

        // 3. Insert DB row after SP succeeds
        var project = new Project
        {
            Id             = Guid.NewGuid(),
            Name           = request.Name,
            Description    = request.Description,
            SchemaName     = schemaName,
            AppId          = appId,
            AppSlug        = appSlug,
            OwnerId        = userId,
            TechStack      = request.TechStack,
            ProjectType    = projectType,
            AuthEnabled    = request.AuthEnabled,
            GitHubRepo     = repoFullName,
            GitHubRepoName = repoName,
            GitHubBranch   = branch,
            GitHubRepoLink = repoLink,
            IsActive       = true,
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow
        };

        var created = await _projectRepo.CreateAsync(project);

        // 4. Seed repo files + push CLAUDE.md (fire-and-forget)
        if (repoFullName is not null)
        {
            _ = _gitHubRepo.SeedProjectFilesAsync(created);

            var (_, renderedMarkdown) = await _claudeConfig.RenderAndStoreTokenAsync(created, userId, actorEmail, baseUrl);
            _ = _gitHubRepo.PushClaudeMdAsync(repoFullName, branch, renderedMarkdown);
        }

        // 5. Create project schema in Supabase (fire-and-forget)
        _ = _dbProxy.CreateSchemaAsync(schemaName);

        return ToResponse(created, "owner");
    }

    public async Task<IEnumerable<ProjectResponse>> GetUserProjectsAsync(Guid userId)
    {
        var appAccess = await _securityPlatform.GetUserAppAccessAsync(userId);

        var canViewAll = appAccess.Any(a =>
            a.AppSlug.Equals("dashboard-hub", StringComparison.OrdinalIgnoreCase) &&
            (a.RoleName.Equals("platform_owner", StringComparison.OrdinalIgnoreCase) ||
             a.Permissions.Contains("view_all_projects", StringComparer.OrdinalIgnoreCase)));

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
            (a.RoleName.Equals("platform_owner", StringComparison.OrdinalIgnoreCase) ||
             a.Permissions.Contains("view_all_projects", StringComparer.OrdinalIgnoreCase)));

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
        if (request.ProjectType is not null)
        {
            if (!ValidProjectTypes.Contains(request.ProjectType.ToLowerInvariant()))
                throw new ArgumentException($"Invalid project_type '{request.ProjectType}'. Must be one of: frontend, backend, database, fullstack.");
            project.ProjectType = request.ProjectType.ToLowerInvariant();
        }
        if (request.AuthEnabled is not null) project.AuthEnabled = request.AuthEnabled.Value;
        project.UpdatedAt = DateTime.UtcNow;

        await _projectRepo.UpdateAsync(project);
        return ToResponse(project);
    }

    public async Task DeactivateProjectAsync(Guid projectId, Guid userId)
    {
        var project = await GetOrThrowAsync(projectId);
        if (project.AppSlug is not null)
        {
            var appAccess = await _securityPlatform.GetUserAppAccessAsync(userId);
            var isPlatformOwner = appAccess.Any(a =>
                a.AppSlug.Equals("dashboard-hub", StringComparison.OrdinalIgnoreCase) &&
                (a.RoleName.Equals("platform_owner", StringComparison.OrdinalIgnoreCase) ||
                 a.Permissions.Contains("view_all_projects", StringComparer.OrdinalIgnoreCase)));

            if (!isPlatformOwner)
            {
                var allowed = await _securityPlatform.AuthorizeAsync(project.AppSlug, projectId.ToString(), "delete_project");
                if (!allowed) throw new UnauthorizedAccessException("You do not have permission to deactivate this project.");
            }
        }

        project.IsActive = false;
        project.UpdatedAt = DateTime.UtcNow;
        await _projectRepo.UpdateAsync(project);
    }

    public async Task<(int pushed, int skipped, List<string> failures)> SyncAllClaudeMdAsync(Guid actorId, string actorEmail, string baseUrl)
    {
        var projects = await _projectRepo.GetAllAsync();
        int pushed = 0, skipped = 0;
        var failures = new List<string>();

        foreach (var project in projects)
        {
            if (project.GitHubRepo is null || project.GitHubBranch is null)
            {
                skipped++;
                continue;
            }

            try
            {
                var (_, renderedMarkdown) = await _claudeConfig.RenderAndStoreTokenAsync(project, actorId, actorEmail, baseUrl);
                await _gitHubRepo.PushClaudeMdAsync(project.GitHubRepo, project.GitHubBranch, renderedMarkdown);
                pushed++;
            }
            catch (Exception ex)
            {
                failures.Add($"{project.Name} ({project.GitHubRepo}): {ex.Message}");
            }
        }

        return (pushed, skipped, failures);
    }

    public async Task<StorageProvisionResponse> ProvisionStorageAsync(Guid projectId, Guid userId)
    {
        var project = await _projectRepo.GetByIdAsync(projectId)
            ?? throw new KeyNotFoundException($"Project {projectId} not found.");

        if (project.AppSlug is not null)
        {
            var allowed = await _securityPlatform.AuthorizeAsync(project.AppSlug, projectId.ToString(), "manage_members");
            if (!allowed) throw new UnauthorizedAccessException("You do not have permission to provision storage for this project.");
        }

        var (bucketName, provisionedAt, _) = await _bucketService.EnsureBucketExistsAsync(project.Id, project.AppSlug);

        if (project.BucketName != bucketName)
            await _projectRepo.UpdateBucketNameAsync(project.Id, bucketName);

        return new StorageProvisionResponse(bucketName, provisionedAt);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly HashSet<string> ValidProjectTypes = ["frontend", "backend", "database", "fullstack"];

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
        Id          = p.Id,
        Name        = p.Name,
        Description = p.Description,
        SchemaName  = p.SchemaName,
        OwnerId     = p.OwnerId,
        AppSlug     = p.AppSlug,
        TechStack   = p.TechStack,
        ProjectType = p.ProjectType,
        AuthEnabled = p.AuthEnabled,
        IsActive    = p.IsActive,
        CreatedAt   = p.CreatedAt,
        RoleName    = roleName,
        GitHub      = p.GitHubRepo is null ? null : new GitHubRepoResponse
        {
            RepoName     = p.GitHubRepoName ?? string.Empty,
            RepoFullName = p.GitHubRepo,
            Branch       = p.GitHubBranch,
            RepoLink     = p.GitHubRepoLink ?? string.Empty
        }
    };
}
