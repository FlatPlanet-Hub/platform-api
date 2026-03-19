using SupabaseProxy.Application.DTOs.Admin;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;

namespace SupabaseProxy.Infrastructure.ExternalServices;

public sealed class AdminRoleService : IAdminRoleService
{
    private readonly IRoleRepository _roleRepo;
    private readonly ICustomRoleRepository _customRoleRepo;
    private readonly IAuditService _audit;

    public AdminRoleService(
        IRoleRepository roleRepo,
        ICustomRoleRepository customRoleRepo,
        IAuditService audit)
    {
        _roleRepo = roleRepo;
        _customRoleRepo = customRoleRepo;
        _audit = audit;
    }

    public async Task<IEnumerable<AdminRoleDto>> ListRolesAsync()
    {
        var systemRoles = await _roleRepo.GetAllAsync();
        var customRoles = await _customRoleRepo.GetAllActiveAsync();

        var result = systemRoles.Select(r => new AdminRoleDto
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description,
            Permissions = [],
            IsSystem = true,
            IsActive = true,
            CreatedAt = r.CreatedAt
        }).Concat(customRoles.Select(r => new AdminRoleDto
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description,
            Permissions = r.Permissions,
            IsSystem = false,
            IsActive = r.IsActive,
            CreatedAt = r.CreatedAt
        }));

        return result.OrderBy(r => r.IsSystem ? 0 : 1).ThenBy(r => r.Name);
    }

    public async Task<AdminRoleDto> CreateRoleAsync(Guid adminId, CreateCustomRoleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Role name is required.");

        var existing = await _customRoleRepo.GetByNameAsync(request.Name);
        if (existing is not null)
            throw new InvalidOperationException($"A role named '{request.Name}' already exists.");

        var role = new CustomRole
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Permissions = request.Permissions.ToArray(),
            CreatedBy = adminId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _customRoleRepo.CreateAsync(role);

        await _audit.LogAsync(adminId, null, "admin.role.created", "custom_roles",
            new { roleName = created.Name });

        return ToDto(created);
    }

    public async Task<AdminRoleDto> UpdateRoleAsync(Guid adminId, Guid roleId, UpdateCustomRoleRequest request)
    {
        var role = await _customRoleRepo.GetByIdAsync(roleId)
            ?? throw new KeyNotFoundException($"Custom role {roleId} not found.");

        if (request.Name is not null)
        {
            var conflict = await _customRoleRepo.GetByNameAsync(request.Name);
            if (conflict is not null && conflict.Id != roleId)
                throw new InvalidOperationException($"A role named '{request.Name}' already exists.");
            role.Name = request.Name;
        }

        if (request.Description is not null) role.Description = request.Description;
        if (request.Permissions is not null) role.Permissions = request.Permissions.ToArray();
        role.UpdatedAt = DateTime.UtcNow;

        await _customRoleRepo.UpdateAsync(role);
        await _audit.LogAsync(adminId, null, "admin.role.updated", "custom_roles",
            new { roleId });

        return ToDto(role);
    }

    public async Task DeactivateRoleAsync(Guid adminId, Guid roleId)
    {
        // Prevent deactivating system roles (they live in platform.roles, not custom_roles)
        var systemRole = await _roleRepo.GetByIdAsync(roleId);
        if (systemRole is not null)
            throw new InvalidOperationException("System roles cannot be deactivated.");

        var role = await _customRoleRepo.GetByIdAsync(roleId)
            ?? throw new KeyNotFoundException($"Custom role {roleId} not found.");

        role.IsActive = false;
        role.UpdatedAt = DateTime.UtcNow;
        await _customRoleRepo.UpdateAsync(role);

        await _audit.LogAsync(adminId, null, "admin.role.deactivated", "custom_roles",
            new { roleId, roleName = role.Name });
    }

    private static AdminRoleDto ToDto(CustomRole r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Description = r.Description,
        Permissions = r.Permissions,
        IsSystem = false,
        IsActive = r.IsActive,
        CreatedAt = r.CreatedAt
    };
}
