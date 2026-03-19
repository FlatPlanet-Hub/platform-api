using Moq;
using SupabaseProxy.Application.DTOs.Admin;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;
using SupabaseProxy.Infrastructure.ExternalServices;

namespace SupabaseProxy.Tests.UnitTests.Services;

public sealed class AdminRoleServiceTests
{
    private readonly Mock<IRoleRepository> _roleRepo = new();
    private readonly Mock<ICustomRoleRepository> _customRoleRepo = new();
    private readonly Mock<IAuditService> _audit = new();

    private AdminRoleService CreateSut() =>
        new(_roleRepo.Object, _customRoleRepo.Object, _audit.Object);

    [Fact]
    public async Task CreateRole_ShouldThrow_WhenNameAlreadyExists()
    {
        var adminId = Guid.NewGuid();
        var request = new CreateCustomRoleRequest { Name = "team_lead", Permissions = ["read"] };
        var existing = new CustomRole { Id = Guid.NewGuid(), Name = "team_lead" };

        _customRoleRepo.Setup(r => r.GetByNameAsync("team_lead")).ReturnsAsync(existing);

        var sut = CreateSut();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.CreateRoleAsync(adminId, request));
    }

    [Fact]
    public async Task CreateRole_ShouldSucceed_WithValidRequest()
    {
        var adminId = Guid.NewGuid();
        var request = new CreateCustomRoleRequest
        {
            Name = "team_lead",
            Description = "Can read and write",
            Permissions = ["read", "write"]
        };

        _customRoleRepo.Setup(r => r.GetByNameAsync("team_lead")).ReturnsAsync((CustomRole?)null);
        _customRoleRepo.Setup(r => r.CreateAsync(It.IsAny<CustomRole>())).ReturnsAsync((CustomRole r) => r);

        var sut = CreateSut();
        var result = await sut.CreateRoleAsync(adminId, request);

        Assert.Equal("team_lead", result.Name);
        Assert.Contains("read", result.Permissions);
        Assert.Contains("write", result.Permissions);
        Assert.False(result.IsSystem);
    }

    [Fact]
    public async Task DeactivateRole_ShouldThrow_WhenRoleIsSystemRole()
    {
        var adminId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var systemRole = new Role { Id = roleId, Name = "platform_admin", IsSystem = true };

        _roleRepo.Setup(r => r.GetByIdAsync(roleId)).ReturnsAsync(systemRole);

        var sut = CreateSut();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.DeactivateRoleAsync(adminId, roleId));
    }

    [Fact]
    public async Task DeactivateRole_ShouldSucceed_ForCustomRole()
    {
        var adminId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var customRole = new CustomRole { Id = roleId, Name = "team_lead", Permissions = ["read"], IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

        _roleRepo.Setup(r => r.GetByIdAsync(roleId)).ReturnsAsync((Role?)null);
        _customRoleRepo.Setup(r => r.GetByIdAsync(roleId)).ReturnsAsync(customRole);
        _customRoleRepo.Setup(r => r.UpdateAsync(It.IsAny<CustomRole>())).Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.DeactivateRoleAsync(adminId, roleId);

        Assert.False(customRole.IsActive);
        _customRoleRepo.Verify(r => r.UpdateAsync(customRole), Times.Once);
    }

    [Fact]
    public async Task ListRoles_ShouldReturnSystemAndCustomRoles()
    {
        var systemRoles = new List<Role>
        {
            new() { Id = Guid.NewGuid(), Name = "platform_admin", IsSystem = true, CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), Name = "user", IsSystem = true, CreatedAt = DateTime.UtcNow }
        };
        var customRoles = new List<CustomRole>
        {
            new() { Id = Guid.NewGuid(), Name = "team_lead", Permissions = ["read", "write"], IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };

        _roleRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(systemRoles);
        _customRoleRepo.Setup(r => r.GetAllActiveAsync()).ReturnsAsync(customRoles);

        var sut = CreateSut();
        var result = (await sut.ListRolesAsync()).ToList();

        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.Name == "platform_admin" && r.IsSystem);
        Assert.Contains(result, r => r.Name == "team_lead" && !r.IsSystem);
    }
}
