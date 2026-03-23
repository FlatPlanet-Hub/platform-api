using Moq;
using FlatPlanet.Platform.Application.DTOs.Project;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using FlatPlanet.Platform.Application.Services;

namespace FlatPlanet.Platform.Tests.UnitTests.Services;

public sealed class ProjectServiceTests
{
    private readonly Mock<IProjectRepository> _projectRepo = new();
    private readonly Mock<IProjectRoleRepository> _roleRepo = new();
    private readonly Mock<IProjectMemberRepository> _memberRepo = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IDbProxyService> _dbProxy = new();
    private readonly Mock<IAuditService> _audit = new();

    private ProjectService CreateSut() =>
        new(_projectRepo.Object, _roleRepo.Object, _memberRepo.Object, _userRepo.Object, _dbProxy.Object, _audit.Object);

    [Fact]
    public async Task CreateProject_ShouldProvisionSchema_AndCreateDefaultRoles()
    {
        var userId = Guid.NewGuid();
        var request = new CreateProjectRequest { Name = "My App", Description = "Test project" };

        _projectRepo.Setup(r => r.CreateAsync(It.IsAny<Project>())).ReturnsAsync((Project p) => p);
        _roleRepo.Setup(r => r.CreateAsync(It.IsAny<ProjectRole>())).ReturnsAsync((ProjectRole r) => r);
        _memberRepo.Setup(r => r.AddAsync(It.IsAny<ProjectMember>())).Returns(Task.CompletedTask);
        _dbProxy.Setup(d => d.CreateSchemaAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var sut = CreateSut();
        var result = await sut.CreateProjectAsync(userId, request);

        Assert.Equal("My App", result.Name);
        Assert.StartsWith("project_", result.SchemaName);
        _dbProxy.Verify(d => d.CreateSchemaAsync(It.Is<string>(s => s.StartsWith("project_"))), Times.Once);
        _roleRepo.Verify(r => r.CreateAsync(It.IsAny<ProjectRole>()), Times.Exactly(3)); // owner, developer, viewer
        _memberRepo.Verify(r => r.AddAsync(It.IsAny<ProjectMember>()), Times.Once);
    }

    [Fact]
    public async Task DeactivateProject_ShouldThrow_WhenRequesterLacksDeletePermission()
    {
        var projectId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var role = new ProjectRole { Id = Guid.NewGuid(), ProjectId = projectId, Name = "developer", Permissions = ["read", "write", "ddl"] };
        var member = new ProjectMember { ProjectId = projectId, UserId = userId, ProjectRoleId = role.Id };
        var project = new Project { Id = projectId, Name = "Test", SchemaName = "project_test", OwnerId = userId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

        _projectRepo.Setup(r => r.GetByIdAsync(projectId)).ReturnsAsync(project);
        _memberRepo.Setup(r => r.GetAsync(projectId, userId)).ReturnsAsync(member);
        _roleRepo.Setup(r => r.GetByIdAsync(projectId, role.Id)).ReturnsAsync(role);

        var sut = CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.DeactivateProjectAsync(projectId, userId));
    }
}
