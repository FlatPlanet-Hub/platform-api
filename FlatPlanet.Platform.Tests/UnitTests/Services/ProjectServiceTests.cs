using Microsoft.Extensions.Logging;
using Moq;
using FlatPlanet.Platform.Application.DTOs.Project;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using FlatPlanet.Platform.Application.Services;

namespace FlatPlanet.Platform.Tests.UnitTests.Services;

public sealed class ProjectServiceTests
{
    private readonly Mock<IProjectRepository> _projectRepo = new();
    private readonly Mock<ISecurityPlatformService> _securityPlatform = new();
    private readonly Mock<IGitHubRepoService> _gitHubRepo = new();
    private readonly Mock<IDbProxyService> _dbProxy = new();
    private readonly Mock<IClaudeConfigService> _claudeConfig = new();
    private readonly Mock<IStorageBucketService> _bucketService = new();
    private readonly Mock<IAuditLogRepository> _auditLog = new();
    private readonly Mock<ILogger<ProjectService>> _logger = new();

    private ProjectService CreateSut() =>
        new(_projectRepo.Object, _securityPlatform.Object, _gitHubRepo.Object, _dbProxy.Object, _claudeConfig.Object, _bucketService.Object, _auditLog.Object, _logger.Object);

    [Fact]
    public async Task CreateProject_ShouldProvisionSchema_AndRegisterApp()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var request = new CreateProjectRequest { Name = "My App", Description = "Test project", GitHub = null };

        _projectRepo.Setup(r => r.CreateAsync(It.IsAny<Project>())).ReturnsAsync((Project p) => p);
        _projectRepo.Setup(r => r.UpdateAsync(It.IsAny<Project>())).Returns(Task.CompletedTask);
        _securityPlatform.Setup(s => s.RegisterAppAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), companyId)).ReturnsAsync(appId);
        _securityPlatform.Setup(s => s.SetupProjectRolesAsync(appId)).Returns(Task.CompletedTask);
        _securityPlatform.Setup(s => s.GrantRoleAsync(appId, userId, "owner")).Returns(Task.CompletedTask);
        _gitHubRepo.Setup(g => g.SeedProjectFilesAsync(It.IsAny<Project>())).Returns(Task.CompletedTask);
        _dbProxy.Setup(d => d.CreateSchemaAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var sut = CreateSut();
        var result = await sut.CreateProjectAsync(userId, "user@example.com", companyId, "https://localhost", request);

        Assert.Equal("My App", result.Name);
        Assert.StartsWith("project_", result.SchemaName);
        Assert.Equal("my-app", result.AppSlug);
        Assert.Null(result.GitHub);
        _securityPlatform.Verify(s => s.RegisterAppAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), companyId), Times.Once);
        _securityPlatform.Verify(s => s.SetupProjectRolesAsync(appId), Times.Once);
        _securityPlatform.Verify(s => s.GrantRoleAsync(appId, userId, "owner"), Times.Once);
        _gitHubRepo.Verify(g => g.CreateRepoAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateProject_WithCreateRepo_ShouldCallCreateRepoAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var request = new CreateProjectRequest
        {
            Name = "My App",
            GitHub = new GitHubRepoRequest { CreateRepo = true, RepoName = "my-app-repo" }
        };

        _projectRepo.Setup(r => r.CreateAsync(It.IsAny<Project>())).ReturnsAsync((Project p) => p);
        _securityPlatform.Setup(s => s.RegisterAppAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), companyId)).ReturnsAsync(appId);
        _securityPlatform.Setup(s => s.SetupProjectRolesAsync(appId)).Returns(Task.CompletedTask);
        _securityPlatform.Setup(s => s.GrantRoleAsync(appId, userId, "owner")).Returns(Task.CompletedTask);
        _gitHubRepo.Setup(g => g.CreateRepoAsync("my-app-repo"))
            .ReturnsAsync(("FlatPlanet-Hub/my-app-repo", "https://github.com/FlatPlanet-Hub/my-app-repo"));
        _gitHubRepo.Setup(g => g.SeedProjectFilesAsync(It.IsAny<Project>())).Returns(Task.CompletedTask);
        _gitHubRepo.Setup(g => g.PushClaudeMdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        _claudeConfig.Setup(c => c.RenderAndStoreTokenAsync(It.IsAny<Project>(), userId, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(("raw-token", "# CLAUDE.md content"));
        _dbProxy.Setup(d => d.CreateSchemaAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var sut = CreateSut();
        var result = await sut.CreateProjectAsync(userId, "user@example.com", companyId, "https://localhost", request);

        _gitHubRepo.Verify(g => g.CreateRepoAsync("my-app-repo"), Times.Once);
        Assert.NotNull(result.GitHub);
        Assert.Equal("my-app-repo", result.GitHub!.RepoName);
        Assert.Equal("FlatPlanet-Hub/my-app-repo", result.GitHub.RepoFullName);
        Assert.Equal("https://github.com/FlatPlanet-Hub/my-app-repo", result.GitHub.RepoLink);
    }

    [Fact]
    public async Task CreateProject_WithExistingRepo_ShouldParseRepoUrl()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var request = new CreateProjectRequest
        {
            Name = "My App",
            GitHub = new GitHubRepoRequest { CreateRepo = false, ExistingRepoUrl = "https://github.com/FlatPlanet-Hub/existing-repo" }
        };

        _projectRepo.Setup(r => r.CreateAsync(It.IsAny<Project>())).ReturnsAsync((Project p) => p);
        _securityPlatform.Setup(s => s.RegisterAppAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), companyId)).ReturnsAsync(appId);
        _securityPlatform.Setup(s => s.SetupProjectRolesAsync(appId)).Returns(Task.CompletedTask);
        _securityPlatform.Setup(s => s.GrantRoleAsync(appId, userId, "owner")).Returns(Task.CompletedTask);
        _gitHubRepo.Setup(g => g.SeedProjectFilesAsync(It.IsAny<Project>())).Returns(Task.CompletedTask);
        _gitHubRepo.Setup(g => g.PushClaudeMdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        _claudeConfig.Setup(c => c.RenderAndStoreTokenAsync(It.IsAny<Project>(), userId, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(("raw-token", "# CLAUDE.md content"));
        _dbProxy.Setup(d => d.CreateSchemaAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var sut = CreateSut();
        var result = await sut.CreateProjectAsync(userId, "user@example.com", companyId, "https://localhost", request);

        _gitHubRepo.Verify(g => g.CreateRepoAsync(It.IsAny<string>()), Times.Never);
        Assert.NotNull(result.GitHub);
        Assert.Equal("existing-repo", result.GitHub!.RepoName);
        Assert.Equal("FlatPlanet-Hub/existing-repo", result.GitHub.RepoFullName);
        Assert.Equal("https://github.com/FlatPlanet-Hub/existing-repo", result.GitHub.RepoLink);
    }

    [Fact]
    public async Task CreateProject_WithNoGitHub_ShouldSkipGitHubCalls()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var request = new CreateProjectRequest { Name = "My App", GitHub = null };

        _projectRepo.Setup(r => r.CreateAsync(It.IsAny<Project>())).ReturnsAsync((Project p) => p);
        _securityPlatform.Setup(s => s.RegisterAppAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), companyId)).ReturnsAsync(appId);
        _securityPlatform.Setup(s => s.SetupProjectRolesAsync(appId)).Returns(Task.CompletedTask);
        _securityPlatform.Setup(s => s.GrantRoleAsync(appId, userId, "owner")).Returns(Task.CompletedTask);
        _dbProxy.Setup(d => d.CreateSchemaAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var sut = CreateSut();
        var result = await sut.CreateProjectAsync(userId, "user@example.com", companyId, "https://localhost", request);

        _gitHubRepo.Verify(g => g.CreateRepoAsync(It.IsAny<string>()), Times.Never);
        _gitHubRepo.Verify(g => g.SeedProjectFilesAsync(It.IsAny<Project>()), Times.Never);
        _gitHubRepo.Verify(g => g.PushClaudeMdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _claudeConfig.Verify(c => c.RenderAndStoreTokenAsync(It.IsAny<Project>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.Null(result.GitHub);
    }

    [Fact]
    public async Task DeactivateProject_ShouldThrow_WhenRequesterLacksDeletePermission()
    {
        var projectId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var project = new Project
        {
            Id = projectId,
            Name = "Test",
            SchemaName = "project_test",
            AppSlug = "test",
            OwnerId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _projectRepo.Setup(r => r.GetByIdAsync(projectId)).ReturnsAsync(project);
        _securityPlatform.Setup(s => s.AuthorizeAsync("test", projectId.ToString(), "delete_project")).ReturnsAsync(false);

        var sut = CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.DeactivateProjectAsync(projectId, userId, "test@flatplanet.com.au"));
    }
}
