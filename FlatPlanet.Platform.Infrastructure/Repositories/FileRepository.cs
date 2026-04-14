using Dapper;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Infrastructure.Repositories;

public class FileRepository : IFileRepository
{
    private readonly IDbConnectionFactory _db;

    public FileRepository(IDbConnectionFactory db) => _db = db;

    public async Task<PlatformFile?> GetByIdAsync(Guid id, Guid? appId = null)
    {
        await using var conn = _db.CreateConnection();
        var sql = "SELECT * FROM platform.files WHERE id = @id::uuid AND is_deleted = FALSE";
        if (appId.HasValue) sql += " AND app_id = @appId::uuid";
        return await conn.QuerySingleOrDefaultAsync<PlatformFile>(sql, new { id, appId });
    }

    public async Task<IEnumerable<PlatformFile>> ListAsync(string businessCode, string? category, string[]? tags, Guid? appId = null, Guid? uploadedBy = null)
    {
        await using var conn = _db.CreateConnection();
        var sql = "SELECT * FROM platform.files WHERE business_code = @businessCode AND is_deleted = FALSE";
        if (appId.HasValue) sql += " AND app_id = @appId::uuid";
        if (uploadedBy.HasValue) sql += " AND uploaded_by = @uploadedBy::uuid";
        if (category != null) sql += " AND category = @category";
        if (tags != null && tags.Length > 0) sql += " AND tags && @tags";
        sql += " ORDER BY created_at DESC";
        return await conn.QueryAsync<PlatformFile>(sql, new { businessCode, appId, uploadedBy, category, tags });
    }

    public async Task<Guid> InsertAsync(PlatformFile file)
    {
        await using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<Guid>("""
            INSERT INTO platform.files
                (id, app_id, business_code, category, original_name, blob_name, content_type, file_size_bytes, uploaded_by, tags, created_at)
            VALUES
                (@Id::uuid, @AppId, @BusinessCode, @Category, @OriginalName, @BlobName, @ContentType, @FileSizeBytes, @UploadedBy::uuid, @Tags, @CreatedAt)
            RETURNING id
            """, new {
            Id = file.Id.ToString(),
            AppId = file.AppId.HasValue ? (object)file.AppId.Value : DBNull.Value,
            file.BusinessCode,
            file.Category,
            file.OriginalName,
            file.BlobName,
            file.ContentType,
            file.FileSizeBytes,
            UploadedBy = file.UploadedBy.ToString(),
            file.Tags,
            file.CreatedAt
        });
    }

    public async Task SoftDeleteAsync(Guid id, DateTime deletedAt)
    {
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE platform.files SET is_deleted = TRUE, deleted_at = @deletedAt WHERE id = @id::uuid",
            new { id, deletedAt });
    }
}
