using SupabaseProxy.Application.DTOs;

namespace SupabaseProxy.Application.Interfaces;

public interface IDbProxyService
{
    Task<IEnumerable<TableInfoDto>> GetTablesAsync(string schema);
    Task<IEnumerable<ColumnInfoDto>> GetColumnsAsync(string schema, string? tableName = null);
    Task<IEnumerable<RelationshipDto>> GetRelationshipsAsync(string schema);
    Task<FullSchemaDto> GetFullSchemaAsync(string schema);

    Task CreateSchemaAsync(string schema);
    Task CreateTableAsync(string schema, CreateTableRequest request);
    Task AlterTableAsync(string schema, AlterTableRequest request);
    Task DropTableAsync(string schema, string tableName);

    Task<IEnumerable<dynamic>> ExecuteReadAsync(string schema, ReadQueryRequest request);
    Task<int> ExecuteWriteAsync(string schema, WriteQueryRequest request);
}
