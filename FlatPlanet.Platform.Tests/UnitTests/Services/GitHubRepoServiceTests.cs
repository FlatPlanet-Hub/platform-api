using Microsoft.Extensions.Options;
using Moq;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;
using FlatPlanet.Platform.Infrastructure.Configuration;
using FlatPlanet.Platform.Infrastructure.ExternalServices;

namespace FlatPlanet.Platform.Tests.UnitTests.Services;

public sealed class GitHubRepoServiceTests
{
    private readonly Mock<IProjectRepository> _projectRepoMock = new();
    private readonly Mock<IDbProxyService> _dbProxyMock = new();

    private GitHubRepoService CreateService() => new(
        _projectRepoMock.Object,
        _dbProxyMock.Object,
        Options.Create(new GitHubSettings { ServiceToken = "fake-service-token", OrgName = "test-org" }));

    // ── SyncDataDictionaryAsync ──────────────────────────────────────────────

    [Fact]
    public async Task SyncDataDictionaryAsync_SkipsSilentlyWhenNoRepoLinked()
    {
        var projectId = Guid.NewGuid();
        var project = new Project { Id = projectId, GitHubRepo = null };

        _projectRepoMock.Setup(r => r.GetByIdAsync(projectId)).ReturnsAsync(project);

        var svc = CreateService();
        await svc.SyncDataDictionaryAsync(projectId, "project_abc123");

        _dbProxyMock.Verify(d => d.GetTablesAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SyncDataDictionaryAsync_SkipsSilentlyWhenProjectNotFound()
    {
        _projectRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Project?)null);

        var svc = CreateService();
        await svc.SyncDataDictionaryAsync(Guid.NewGuid(), "project_abc123");

        _dbProxyMock.Verify(d => d.GetTablesAsync(It.IsAny<string>()), Times.Never);
    }
}
