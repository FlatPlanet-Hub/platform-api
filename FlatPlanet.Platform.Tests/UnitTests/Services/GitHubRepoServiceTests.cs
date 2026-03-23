using Moq;
using FlatPlanet.Platform.Application.DTOs.Repo;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using FlatPlanet.Platform.Infrastructure.ExternalServices;

namespace FlatPlanet.Platform.Tests.UnitTests.Services;

public sealed class GitHubRepoServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IProjectRepository> _projectRepoMock = new();
    private readonly Mock<IAuditService> _auditMock = new();
    private readonly Mock<IDbProxyService> _dbProxyMock = new();
    private readonly Mock<IEncryptionService> _encryptionMock = new();
    private readonly Mock<IUserOAuthLinkRepository> _oauthLinkRepoMock = new();
    private readonly Mock<IOAuthProviderRepository> _oauthProviderRepoMock = new();
    private readonly Mock<IUserAppRoleRepository> _userAppRoleRepoMock = new();
    private readonly Mock<IRolePermissionRepository> _rolePermRepoMock = new();

    private GitHubRepoService CreateService() => new(
        _userRepoMock.Object,
        _projectRepoMock.Object,
        _auditMock.Object,
        _dbProxyMock.Object,
        _encryptionMock.Object,
        _oauthLinkRepoMock.Object,
        _oauthProviderRepoMock.Object,
        _userAppRoleRepoMock.Object,
        _rolePermRepoMock.Object);

    // ── CreateRepoAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRepoAsync_ThrowsWhenProjectNotFound()
    {
        _projectRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Project?)null);

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateRepoAsync(Guid.NewGuid(), Guid.NewGuid(),
                new CreateRepoRequest("my-repo", null)));
    }

    [Fact]
    public async Task CreateRepoAsync_ThrowsWhenRepoAlreadyLinked()
    {
        var userId = Guid.NewGuid();
        var project = new Project { Id = Guid.NewGuid(), OwnerId = userId, GitHubRepo = "org/already-linked" };
        _projectRepoMock.Setup(r => r.GetByIdAsync(project.Id)).ReturnsAsync(project);

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateRepoAsync(userId, project.Id, new CreateRepoRequest("my-repo", null)));
    }

    [Fact]
    public async Task CreateRepoAsync_ThrowsWhenUserNotFound()
    {
        var userId = Guid.NewGuid();
        var project = new Project { Id = Guid.NewGuid(), OwnerId = userId, GitHubRepo = null };
        _projectRepoMock.Setup(r => r.GetByIdAsync(project.Id)).ReturnsAsync(project);
        _userRepoMock.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync((User?)null);

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateRepoAsync(userId, project.Id, new CreateRepoRequest("my-repo", null)));
    }

    [Fact]
    public async Task CreateRepoAsync_ThrowsWhenNoGitHubTokenOnFile()
    {
        var userId = Guid.NewGuid();
        var project = new Project { Id = Guid.NewGuid(), OwnerId = userId, GitHubRepo = null };
        var user = new User { Id = userId, GitHubId = 12345 };
        var provider = new OAuthProvider { Id = Guid.NewGuid(), Name = "github" };

        _projectRepoMock.Setup(r => r.GetByIdAsync(project.Id)).ReturnsAsync(project);
        _userRepoMock.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);
        _oauthProviderRepoMock.Setup(r => r.GetByNameAsync("github")).ReturnsAsync(provider);
        _oauthLinkRepoMock
            .Setup(r => r.GetByProviderUserIdAsync(provider.Id, user.GitHubId.ToString()))
            .ReturnsAsync((UserOAuthLink?)null);

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateRepoAsync(userId, project.Id, new CreateRepoRequest("my-repo", null)));
    }

    // ── GetRepoAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRepoAsync_ThrowsWhenNoRepoLinked()
    {
        var userId = Guid.NewGuid();
        var project = new Project { Id = Guid.NewGuid(), OwnerId = userId, GitHubRepo = null };
        var user = new User { Id = userId, GitHubId = 12345 };
        var provider = new OAuthProvider { Id = Guid.NewGuid(), Name = "github" };
        var oauthLink = new UserOAuthLink { Id = Guid.NewGuid(), AccessTokenEncrypted = "encrypted" };

        _projectRepoMock.Setup(r => r.GetByIdAsync(project.Id)).ReturnsAsync(project);
        _userRepoMock.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);
        _oauthProviderRepoMock.Setup(r => r.GetByNameAsync("github")).ReturnsAsync(provider);
        _oauthLinkRepoMock
            .Setup(r => r.GetByProviderUserIdAsync(provider.Id, user.GitHubId.ToString()))
            .ReturnsAsync(oauthLink);
        _encryptionMock.Setup(e => e.Decrypt("encrypted")).Returns("fake-token");

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.GetRepoAsync(userId, project.Id));
    }

    // ── DeleteRepoAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRepoAsync_ThrowsWhenProjectNotFound()
    {
        _projectRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Project?)null);

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.DeleteRepoAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    // ── CreateMultiFileCommitAsync ───────────────────────────────────────────

    [Fact]
    public async Task CreateMultiFileCommitAsync_ThrowsWhenNoFiles()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateMultiFileCommitAsync(Guid.NewGuid(), Guid.NewGuid(),
                new CreateCommitRequest("msg", "main", [])));
    }

    // ── SyncDataDictionaryAsync ──────────────────────────────────────────────

    [Fact]
    public async Task SyncDataDictionaryAsync_SkipsSilentlyWhenNoRepoLinked()
    {
        var projectId = Guid.NewGuid();
        var project = new Project { Id = projectId, GitHubRepo = null };

        _projectRepoMock.Setup(r => r.GetByIdAsync(projectId)).ReturnsAsync(project);

        var svc = CreateService();
        await svc.SyncDataDictionaryAsync(Guid.NewGuid(), projectId, "project_abc123");

        _dbProxyMock.Verify(d => d.GetTablesAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SyncDataDictionaryAsync_SkipsSilentlyWhenProjectNotFound()
    {
        _projectRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Project?)null);

        var svc = CreateService();
        await svc.SyncDataDictionaryAsync(Guid.NewGuid(), Guid.NewGuid(), "project_abc123");

        _dbProxyMock.Verify(d => d.GetTablesAsync(It.IsAny<string>()), Times.Never);
    }

    // ── GetTreeAsync guard ───────────────────────────────────────────────────

    [Fact]
    public async Task GetTreeAsync_ThrowsWhenNoRepoLinked()
    {
        var userId = Guid.NewGuid();
        var project = new Project { Id = Guid.NewGuid(), OwnerId = userId, GitHubRepo = null };
        var user = new User { Id = userId, GitHubId = 12345 };
        var provider = new OAuthProvider { Id = Guid.NewGuid(), Name = "github" };
        var oauthLink = new UserOAuthLink { Id = Guid.NewGuid(), AccessTokenEncrypted = "encrypted" };

        _projectRepoMock.Setup(r => r.GetByIdAsync(project.Id)).ReturnsAsync(project);
        _userRepoMock.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);
        _oauthProviderRepoMock.Setup(r => r.GetByNameAsync("github")).ReturnsAsync(provider);
        _oauthLinkRepoMock
            .Setup(r => r.GetByProviderUserIdAsync(provider.Id, user.GitHubId.ToString()))
            .ReturnsAsync(oauthLink);
        _encryptionMock.Setup(e => e.Decrypt("encrypted")).Returns("fake-token");

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.GetTreeAsync(userId, project.Id, null));
    }
}
