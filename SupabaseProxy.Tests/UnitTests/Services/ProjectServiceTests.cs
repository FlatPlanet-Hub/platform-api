using Moq;
using SupabaseProxy.Application.DTOs.Project;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;
using SupabaseProxy.Infrastructure.ExternalServices;

namespace SupabaseProxy.Tests.UnitTests.Services;

public sealed class ProjectServiceTests
{
    private readonly Mock<IProjectRepository> _projectRepo = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IDbProxyService> _dbProxy = new();
    private readonly Mock<IAuditService> _audit = new();

    private ProjectService CreateSut() =>
        new(_projectRepo.Object, _userRepo.Object, _dbProxy.Object, _audit.Object);

    [Fact]
    public async Task CreateProject_ShouldProvisionSchema_AndCreateDefaultRoles()
    {
        var userId = Guid.NewGuid();
        var request = new CreateProjectRequest { Name = "My App", Description = "Test project" };

        _projectRepo.Setup(r => r.CreateAsync(It.IsAny<Project>())).ReturnsAsync((Project p) => p);
        _projectRepo.Setup(r => r.CreateRoleAsync(It.IsAny<ProjectRole>())).ReturnsAsync((ProjectRole r) => r);
        _projectRepo.Setup(r => r.AddMemberAsync(It.IsAny<ProjectMember>())).Returns(Task.CompletedTask);
        _dbProxy.Setup(d => d.CreateSchemaAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var sut = CreateSut();
        var result = await sut.CreateProjectAsync(userId, request);

        Assert.Equal("My App", result.Name);
        Assert.StartsWith("project_", result.SchemaName);
        _dbProxy.Verify(d => d.CreateSchemaAsync(It.Is<string>(s => s.StartsWith("project_"))), Times.Once);
        _projectRepo.Verify(r => r.CreateRoleAsync(It.IsAny<ProjectRole>()), Times.Exactly(3)); // owner, developer, viewer
        _projectRepo.Verify(r => r.AddMemberAsync(It.IsAny<ProjectMember>()), Times.Once);
    }

    [Fact]
    public async Task InviteMember_ShouldThrow_WhenRequesterLacksManageMembersPermission()
    {
        var projectId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var role = new ProjectRole { Id = Guid.NewGuid(), ProjectId = projectId, Name = "viewer", Permissions = ["read"] };
        var member = new ProjectMember { ProjectId = projectId, UserId = requesterId, ProjectRoleId = role.Id };

        _projectRepo.Setup(r => r.GetMemberAsync(projectId, requesterId)).ReturnsAsync(member);
        _projectRepo.Setup(r => r.GetRoleAsync(projectId, role.Id)).ReturnsAsync(role);

        var sut = CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.InviteMemberAsync(projectId, requesterId, new InviteUserRequest { GitHubUsername = "someone", Role = "developer" }));
    }

    [Fact]
    public async Task InviteMember_ShouldThrow_WhenUserAlreadyMember()
    {
        var projectId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var targetUser = new User { Id = Guid.NewGuid(), GitHubUsername = "existing" };
        var ownerRole = new ProjectRole { Id = Guid.NewGuid(), ProjectId = projectId, Name = "owner", Permissions = ["read", "write", "ddl", "manage_members", "delete_project"] };
        var requesterMember = new ProjectMember { ProjectId = projectId, UserId = requesterId, ProjectRoleId = ownerRole.Id };
        var existingMember = new ProjectMember { ProjectId = projectId, UserId = targetUser.Id };

        _projectRepo.Setup(r => r.GetMemberAsync(projectId, requesterId)).ReturnsAsync(requesterMember);
        _projectRepo.Setup(r => r.GetRoleAsync(projectId, ownerRole.Id)).ReturnsAsync(ownerRole);
        _userRepo.Setup(r => r.GetByGitHubUsernameAsync("existing")).ReturnsAsync(targetUser);
        _projectRepo.Setup(r => r.GetMemberAsync(projectId, targetUser.Id)).ReturnsAsync(existingMember);

        var sut = CreateSut();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.InviteMemberAsync(projectId, requesterId, new InviteUserRequest { GitHubUsername = "existing", Role = "developer" }));
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
        _projectRepo.Setup(r => r.GetMemberAsync(projectId, userId)).ReturnsAsync(member);
        _projectRepo.Setup(r => r.GetRoleAsync(projectId, role.Id)).ReturnsAsync(role);

        var sut = CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.DeactivateProjectAsync(projectId, userId));
    }

    [Fact]
    public async Task DeleteProjectRole_ShouldThrow_WhenDeletingDefaultRole()
    {
        var projectId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ownerRole = new ProjectRole { Id = Guid.NewGuid(), Permissions = ["read", "write", "ddl", "manage_members", "delete_project"] };
        var defaultRole = new ProjectRole { Id = roleId, ProjectId = projectId, Name = "viewer", Permissions = ["read"], IsDefault = true };
        var member = new ProjectMember { ProjectId = projectId, UserId = userId, ProjectRoleId = ownerRole.Id };

        _projectRepo.Setup(r => r.GetMemberAsync(projectId, userId)).ReturnsAsync(member);
        _projectRepo.Setup(r => r.GetRoleAsync(projectId, ownerRole.Id)).ReturnsAsync(ownerRole);
        _projectRepo.Setup(r => r.GetRoleAsync(projectId, roleId)).ReturnsAsync(defaultRole);

        var sut = CreateSut();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.DeleteProjectRoleAsync(projectId, roleId, userId));
    }
}
