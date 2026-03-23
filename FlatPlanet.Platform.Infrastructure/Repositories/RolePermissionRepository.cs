using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public sealed class RolePermissionRepository(IDbConnectionFactory connectionFactory) : IRolePermissionRepository
{
    public async Task AssignAsync(Guid roleId, IEnumerable<Guid> permissionIds, Guid? grantedBy)
    {
        using var conn = connectionFactory.CreateConnection();
        foreach (var permId in permissionIds)
        {
            await conn.ExecuteAsync("""
                INSERT INTO platform.role_permissions (id, role_id, permission_id, granted_by, created_at)
                VALUES (gen_random_uuid(), @RoleId, @PermId, @GrantedBy, now())
                ON CONFLICT (role_id, permission_id) DO NOTHING
                """, new { RoleId = roleId, PermId = permId, GrantedBy = grantedBy });
        }
    }

    public async Task RemoveAsync(Guid roleId, Guid permissionId)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM platform.role_permissions WHERE role_id = @RoleId AND permission_id = @PermId",
            new { RoleId = roleId, PermId = permissionId });
    }

    public async Task<IEnumerable<RolePermission>> GetByRoleIdAsync(Guid roleId)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<RolePermission>(
            "SELECT * FROM platform.role_permissions WHERE role_id = @RoleId",
            new { RoleId = roleId });
    }

    public async Task<IEnumerable<string>> GetPermissionNamesByRoleIdAsync(Guid roleId)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<string>("""
            SELECT p.name FROM platform.role_permissions rp
            INNER JOIN platform.permissions p ON p.id = rp.permission_id
            WHERE rp.role_id = @RoleId
            """, new { RoleId = roleId });
    }
}
