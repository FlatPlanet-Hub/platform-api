using Moq;
using FlatPlanet.Platform.Application.DTOs.Auth;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Application.Services;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Tests.UnitTests.Services;

public sealed class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IRoleRepository> _roleRepo = new();
    private readonly Mock<IProjectRepository> _projectRepo = new();
    private readonly Mock<IProjectMemberRepository> _memberRepo = new();
    private readonly Mock<IProjectRoleRepository> _roleRepoProject = new();
    private readonly Mock<ICustomRoleRepository> _customRoleRepo = new();
    private readonly Mock<IAuditService> _audit = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<IUserAppRoleRepository> _userAppRoleRepo = new();
    private readonly Mock<IAppRepository> _appRepo = new();
    private readonly Mock<IRolePermissionRepository> _rolePermRepo = new();
    private readonly Mock<IUserOAuthLinkRepository> _oauthLinkRepo = new();
    private readonly Mock<IOAuthProviderRepository> _oauthProviderRepo = new();

    private UserService CreateSut() =>
        new(_userRepo.Object, _roleRepo.Object, _projectRepo.Object, _memberRepo.Object, _roleRepoProject.Object, _customRoleRepo.Object, _audit.Object, _encryption.Object, _userAppRoleRepo.Object, _appRepo.Object, _rolePermRepo.Object, _oauthLinkRepo.Object, _oauthProviderRepo.Object);

    [Fact]
    public async Task UpsertFromGitHub_ShouldThrow_WhenUserNotOnboarded()
    {
        var profile = new GitHubUserProfile { Id = 12345, Login = "johndoe", Name = "John Doe", AccessToken = "ghp_token" };

        _userRepo.Setup(r => r.GetByGitHubIdAsync(profile.Id)).ReturnsAsync((User?)null);

        var sut = CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.UpsertFromGitHubAsync(profile));

        _userRepo.Verify(r => r.CreateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task UpsertFromGitHub_ShouldUpdateUser_WhenUserExists()
    {
        var existing = new User { Id = Guid.NewGuid(), GitHubId = 12345, GitHubUsername = "old", FirstName = "Old", LastName = "Name", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var profile = new GitHubUserProfile { Id = 12345, Login = "johndoe", Name = "John Doe", AccessToken = "ghp_token" };
        var provider = new OAuthProvider { Id = Guid.NewGuid(), Name = "github" };

        _userRepo.Setup(r => r.GetByGitHubIdAsync(profile.Id)).ReturnsAsync(existing);
        _userRepo.Setup(r => r.UpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);
        _oauthProviderRepo.Setup(r => r.GetByNameAsync("github")).ReturnsAsync(provider);
        _oauthLinkRepo.Setup(r => r.GetByProviderUserIdAsync(provider.Id, profile.Id.ToString())).ReturnsAsync((UserOAuthLink?)null);
        _oauthLinkRepo.Setup(r => r.CreateAsync(It.IsAny<UserOAuthLink>())).ReturnsAsync((UserOAuthLink l) => l);
        _encryption.Setup(e => e.Encrypt(profile.AccessToken)).Returns("encrypted_token");

        var sut = CreateSut();
        var result = await sut.UpsertFromGitHubAsync(profile);

        // GitHub username is updated; admin-set first/last name preserved
        Assert.Equal("johndoe", result.GitHubUsername);
        Assert.Equal("Old", result.FirstName);
        Assert.Equal("Name", result.LastName);
        _userRepo.Verify(r => r.UpdateAsync(existing), Times.Once);
        _userRepo.Verify(r => r.CreateAsync(It.IsAny<User>()), Times.Never);
        _oauthLinkRepo.Verify(r => r.CreateAsync(It.IsAny<UserOAuthLink>()), Times.Once);
    }

    [Fact]
    public async Task AssignSystemRole_ShouldThrow_WhenRequesterIsNotAdmin()
    {
        var requesterId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetSystemRolesAsync(requesterId)).ReturnsAsync(["user"]);

        var sut = CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.AssignSystemRoleAsync(requesterId, new RoleAssignRequest { UserId = Guid.NewGuid(), RoleName = "platform_admin" }));
    }

    [Fact]
    public async Task AssignSystemRole_ShouldSucceed_WhenRequesterIsAdmin()
    {
        var requesterId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var role = new Role { Id = Guid.NewGuid(), Name = "platform_admin" };

        _userRepo.Setup(r => r.GetSystemRolesAsync(requesterId)).ReturnsAsync(["platform_admin"]);
        _roleRepo.Setup(r => r.GetByNameAsync("platform_admin")).ReturnsAsync(role);
        _userRepo.Setup(r => r.AssignSystemRoleAsync(targetId, role.Id, requesterId)).Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.AssignSystemRoleAsync(requesterId, new RoleAssignRequest { UserId = targetId, RoleName = "platform_admin" });

        _userRepo.Verify(r => r.AssignSystemRoleAsync(targetId, role.Id, requesterId), Times.Once);
    }

    [Fact]
    public async Task RevokeSystemRole_ShouldThrow_WhenRequesterIsNotAdmin()
    {
        var requesterId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetSystemRolesAsync(requesterId)).ReturnsAsync(["user"]);

        var sut = CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.RevokeSystemRoleAsync(requesterId, new RoleRevokeRequest { UserId = Guid.NewGuid(), RoleName = "platform_admin" }));
    }
}
