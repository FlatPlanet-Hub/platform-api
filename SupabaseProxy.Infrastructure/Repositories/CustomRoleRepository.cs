using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Domain.Entities;
using SupabaseProxy.Infrastructure.Configuration;

namespace SupabaseProxy.Infrastructure.Repositories;

public sealed class CustomRoleRepository : ICustomRoleRepository
{
    private readonly SupabaseSettings _settings;

    public CustomRoleRepository(IOptions<SupabaseSettings> settings) => _settings = settings.Value;

    private NpgsqlConnection CreateConnection() => new(_settings.BuildConnectionString());

    public async Task<IEnumerable<CustomRole>> GetAllActiveAsync()
    {
        await using var conn = CreateConnection();
        return await conn.QueryAsync<CustomRole>(
            "SELECT * FROM platform.custom_roles WHERE is_active = true ORDER BY name");
    }

    public async Task<CustomRole?> GetByIdAsync(Guid id)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<CustomRole>(
            "SELECT * FROM platform.custom_roles WHERE id = @id", new { id });
    }

    public async Task<CustomRole?> GetByNameAsync(string name)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<CustomRole>(
            "SELECT * FROM platform.custom_roles WHERE name = @name", new { name });
    }

    public async Task<CustomRole> CreateAsync(CustomRole role)
    {
        await using var conn = CreateConnection();
        const string sql = """
            INSERT INTO platform.custom_roles (id, name, description, permissions, created_by, is_active, created_at, updated_at)
            VALUES (@Id, @Name, @Description, @Permissions, @CreatedBy, @IsActive, @CreatedAt, @UpdatedAt)
            RETURNING *
            """;
        return await conn.QuerySingleAsync<CustomRole>(sql, role);
    }

    public async Task UpdateAsync(CustomRole role)
    {
        await using var conn = CreateConnection();
        const string sql = """
            UPDATE platform.custom_roles
            SET name = @Name, description = @Description, permissions = @Permissions,
                is_active = @IsActive, updated_at = @UpdatedAt
            WHERE id = @Id
            """;
        await conn.ExecuteAsync(sql, role);
    }

    public async Task<IEnumerable<CustomRole>> GetByUserIdAsync(Guid userId)
    {
        await using var conn = CreateConnection();
        const string sql = """
            SELECT cr.* FROM platform.custom_roles cr
            JOIN platform.user_custom_roles ucr ON ucr.custom_role_id = cr.id
            WHERE ucr.user_id = @userId AND cr.is_active = true
            """;
        return await conn.QueryAsync<CustomRole>(sql, new { userId });
    }

    public async Task AssignToUserAsync(Guid userId, Guid customRoleId, Guid assignedBy)
    {
        await using var conn = CreateConnection();
        const string sql = """
            INSERT INTO platform.user_custom_roles (id, user_id, custom_role_id, assigned_by, assigned_at)
            VALUES (gen_random_uuid(), @userId, @customRoleId, @assignedBy, now())
            ON CONFLICT (user_id, custom_role_id) DO NOTHING
            """;
        await conn.ExecuteAsync(sql, new { userId, customRoleId, assignedBy });
    }

    public async Task RevokeFromUserAsync(Guid userId, Guid customRoleId)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM platform.user_custom_roles WHERE user_id = @userId AND custom_role_id = @customRoleId",
            new { userId, customRoleId });
    }

    public async Task RevokeAllFromUserAsync(Guid userId)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM platform.user_custom_roles WHERE user_id = @userId",
            new { userId });
    }

    public async Task SetUserCustomRolesAsync(Guid userId, IEnumerable<Guid> customRoleIds, Guid assignedBy)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync(
            "DELETE FROM platform.user_custom_roles WHERE user_id = @userId",
            new { userId }, tx);

        foreach (var roleId in customRoleIds)
        {
            await conn.ExecuteAsync("""
                INSERT INTO platform.user_custom_roles (id, user_id, custom_role_id, assigned_by, assigned_at)
                VALUES (gen_random_uuid(), @userId, @roleId, @assignedBy, now())
                ON CONFLICT (user_id, custom_role_id) DO NOTHING
                """, new { userId, roleId, assignedBy }, tx);
        }

        await tx.CommitAsync();
    }
}
