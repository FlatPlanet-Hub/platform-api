using Moq;
using FlatPlanet.Platform.Application.DTOs.Admin;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using FlatPlanet.Platform.Application.Services;

namespace FlatPlanet.Platform.Tests.UnitTests.Services;

public sealed class AdminUserServiceTests
{
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IProjectMemberRepository> _memberRepo = new();
    private readonly Mock<IRoleRepository> _roleRepo = new();
    private readonly Mock<ICustomRoleRepository> _customRoleRepo = new();
    private readonly Mock<IAuditService> _audit = new();
    private readonly Mock<IUserAppRoleRepository> _userAppRoleRepo = new();

    private AdminUserService CreateSut() =>
        new(_userRepo.Object, _memberRepo.Object, _roleRepo.Object, _customRoleRepo.Object, _audit.Object, _userAppRoleRepo.Object);

    [Fact]
    public async Task CreateUser_ShouldThrow_WhenGitHubIdAlreadyExists()
    {
        var adminId = Guid.NewGuid();
        var request = new CreateAdminUserRequest { GitHubId = 12345, GitHubUsername = "existing" };
        var existing = new User { Id = Guid.NewGuid(), GitHubId = 12345 };

        _userRepo.Setup(r => r.GetByGitHubIdAsync(12345)).ReturnsAsync(existing);

        var sut = CreateSut();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.CreateUserAsync(adminId, request));
    }

    [Fact]
    public async Task CreateUser_ShouldAssignDefaultUserRole_OnSuccess()
    {
        var adminId = Guid.NewGuid();
        var request = new CreateAdminUserRequest
        {
            GitHubId = 99999,
            GitHubUsername = "newuser",
            FirstName = "New",
            LastName = "User"
        };
        var userRole = new Role { Id = Guid.NewGuid(), Name = "user" };

        _userRepo.Setup(r => r.GetByGitHubIdAsync(99999)).ReturnsAsync((User?)null);
        _userRepo.Setup(r => r.CreateAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
        _userRepo.Setup(r => r.GetSystemRoleEntitiesAsync(It.IsAny<Guid>())).ReturnsAsync([userRole]);
        _userRepo.Setup(r => r.GetProjectMembershipsAsync(It.IsAny<Guid>())).ReturnsAsync([]);
        _roleRepo.Setup(r => r.GetByNameAsync("user")).ReturnsAsync(userRole);

        var sut = CreateSut();
        var result = await sut.CreateUserAsync(adminId, request);

        Assert.Equal("newuser", result.GitHubUsername);
        Assert.Equal("New", result.FirstName);
        _userRepo.Verify(r => r.AssignSystemRoleAsync(It.IsAny<Guid>(), userRole.Id, adminId), Times.Once);
    }

    [Fact]
    public async Task CreateUser_ShouldThrow_WhenGitHubIdIsNotPositive()
    {
        var adminId = Guid.NewGuid();
        var request = new CreateAdminUserRequest { GitHubId = 0, GitHubUsername = "baduser" };

        _userRepo.Setup(r => r.GetByGitHubIdAsync(0)).ReturnsAsync((User?)null);

        var sut = CreateSut();
        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() =>
            sut.CreateUserAsync(adminId, request));
    }

    [Fact]
    public async Task DeactivateUser_ShouldThrow_WhenAdminDeactivatesSelf()
    {
        var adminId = Guid.NewGuid();

        var sut = CreateSut();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.DeactivateUserAsync(adminId, adminId));
    }

    [Fact]
    public async Task DeactivateUser_ShouldRevokeAllTokens()
    {
        var adminId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, GitHubUsername = "target", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);
        _userRepo.Setup(r => r.UpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);
        _userRepo.Setup(r => r.RevokeAllTokensAsync(userId)).Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.DeactivateUserAsync(adminId, userId);

        Assert.False(user.IsActive);
        _userRepo.Verify(r => r.RevokeAllTokensAsync(userId), Times.Once);
    }

    [Fact]
    public async Task UpdateUser_ShouldThrow_WhenAdminDeactivatesSelf()
    {
        var adminId = Guid.NewGuid();
        var request = new UpdateAdminUserRequest { IsActive = false };

        var sut = CreateSut();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UpdateUserAsync(adminId, adminId, request));
    }

    [Fact]
    public async Task UpdateUserRoles_ShouldThrow_WhenAdminChangesOwnRoles()
    {
        var adminId = Guid.NewGuid();
        var request = new UpdateUserRolesRequest { RoleIds = [Guid.NewGuid()] };

        var sut = CreateSut();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UpdateUserRolesAsync(adminId, adminId, request));
    }
}
