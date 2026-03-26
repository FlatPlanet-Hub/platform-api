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

    private ProjectService CreateSut() =>
        new(_projectRepo.Object, _securityPlatform.Object, _gitHubRepo.Object, _dbProxy.Object);

    [Fact]
    public async Task CreateProject_ShouldProvisionSchema_AndRegisterApp()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var request = new CreateProjectRequest { Name = "My App", Description = "Test project" };

        _projectRepo.Setup(r => r.CreateAsync(It.IsAny<Project>())).ReturnsAsync((Project p) => p);
        _projectRepo.Setup(r => r.UpdateAsync(It.IsAny<Project>())).Returns(Task.CompletedTask);
        _securityPlatform.Setup(s => s.RegisterAppAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), companyId)).ReturnsAsync(appId);
        _securityPlatform.Setup(s => s.SetupProjectRolesAsync(appId)).Returns(Task.CompletedTask);
        _securityPlatform.Setup(s => s.GrantRoleAsync(appId, userId, "owner")).Returns(Task.CompletedTask);
        _gitHubRepo.Setup(g => g.SeedProjectFilesAsync(It.IsAny<Project>())).Returns(Task.CompletedTask);
        _dbProxy.Setup(d => d.CreateSchemaAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var sut = CreateSut();
        var result = await sut.CreateProjectAsync(userId, companyId, "https://localhost", request);

        Assert.Equal("My App", result.Name);
        Assert.StartsWith("project_", result.SchemaName);
        Assert.Equal("my-app", result.AppSlug);
        _securityPlatform.Verify(s => s.RegisterAppAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), companyId), Times.Once);
        _securityPlatform.Verify(s => s.SetupProjectRolesAsync(appId), Times.Once);
        _securityPlatform.Verify(s => s.GrantRoleAsync(appId, userId, "owner"), Times.Once);
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
            sut.DeactivateProjectAsync(projectId, userId));
    }
}
