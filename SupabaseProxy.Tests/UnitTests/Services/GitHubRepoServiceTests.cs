using Moq;
using Microsoft.Extensions.Options;
using SupabaseProxy.Application.DTOs.Repo;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;
using SupabaseProxy.Infrastructure.Common.Helpers;
using SupabaseProxy.Infrastructure.Configuration;
using SupabaseProxy.Infrastructure.ExternalServices;

namespace SupabaseProxy.Tests.UnitTests.Services;

public sealed class GitHubRepoServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IProjectRepository> _projectRepoMock = new();
    private readonly Mock<IAuditService> _auditMock = new();
    private readonly Mock<IDbProxyService> _dbProxyMock = new();
    private readonly EncryptionSettings _encryption = new() { Key = "12345678901234567890123456789012" };

    private GitHubRepoService CreateService() => new(
        _userRepoMock.Object,
        _projectRepoMock.Object,
        _auditMock.Object,
        _dbProxyMock.Object,
        Options.Create(_encryption));

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
        var project = new Project { Id = Guid.NewGuid(), GitHubRepo = "org/already-linked" };
        _projectRepoMock.Setup(r => r.GetByIdAsync(project.Id)).ReturnsAsync(project);

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateRepoAsync(Guid.NewGuid(), project.Id,
                new CreateRepoRequest("my-repo", null)));
    }

    [Fact]
    public async Task CreateRepoAsync_ThrowsWhenUserNotFound()
    {
        var project = new Project { Id = Guid.NewGuid(), GitHubRepo = null };
        _projectRepoMock.Setup(r => r.GetByIdAsync(project.Id)).ReturnsAsync(project);
        _userRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateRepoAsync(Guid.NewGuid(), project.Id,
                new CreateRepoRequest("my-repo", null)));
    }

    [Fact]
    public async Task CreateRepoAsync_ThrowsWhenNoGitHubTokenOnFile()
    {
        var project = new Project { Id = Guid.NewGuid(), GitHubRepo = null };
        var user = new User { Id = Guid.NewGuid(), GitHubAccessToken = null };

        _projectRepoMock.Setup(r => r.GetByIdAsync(project.Id)).ReturnsAsync(project);
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateRepoAsync(user.Id, project.Id,
                new CreateRepoRequest("my-repo", null)));
    }

    // ── GetRepoAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRepoAsync_ThrowsWhenNoRepoLinked()
    {
        var userId = Guid.NewGuid();
        var project = new Project { Id = Guid.NewGuid(), GitHubRepo = null };
        var user = new User
        {
            Id = userId,
            GitHubAccessToken = EncryptionHelper.Encrypt("fake-token", _encryption.Key)
        };

        _projectRepoMock.Setup(r => r.GetByIdAsync(project.Id)).ReturnsAsync(project);
        _userRepoMock.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);

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
        var userId = Guid.NewGuid();
        var project = new Project { Id = Guid.NewGuid(), GitHubRepo = "org/repo" };
        var user = new User
        {
            Id = userId,
            GitHubAccessToken = EncryptionHelper.Encrypt("fake-token", _encryption.Key)
        };

        _projectRepoMock.Setup(r => r.GetByIdAsync(project.Id)).ReturnsAsync(project);
        _userRepoMock.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);

        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateMultiFileCommitAsync(userId, project.Id,
                new CreateCommitRequest("msg", "main", [])));
    }

    // ── DeleteBranchAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task SyncDataDictionaryAsync_SkipsSilentlyWhenNoRepoLinked()
    {
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var project = new Project { Id = projectId, GitHubRepo = null };

        _projectRepoMock.Setup(r => r.GetByIdAsync(projectId)).ReturnsAsync(project);

        var svc = CreateService();
        // Should complete without throwing
        await svc.SyncDataDictionaryAsync(userId, projectId, "project_abc123");

        // DB proxy should not be called if there's no repo
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
        var project = new Project { Id = Guid.NewGuid(), GitHubRepo = null };
        var user = new User
        {
            Id = userId,
            GitHubAccessToken = EncryptionHelper.Encrypt("fake-token", _encryption.Key)
        };

        _projectRepoMock.Setup(r => r.GetByIdAsync(project.Id)).ReturnsAsync(project);
        _userRepoMock.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.GetTreeAsync(userId, project.Id, null));
    }
}
