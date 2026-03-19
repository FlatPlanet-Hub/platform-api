namespace SupabaseProxy.Application.DTOs;

public sealed class TableInfoDto
{
    public string TableName { get; init; } = string.Empty;
    public string TableType { get; init; } = string.Empty;
}

public sealed class ColumnInfoDto
{
    public string TableName { get; init; } = string.Empty;
    public string ColumnName { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public bool IsNullable { get; init; }
    public string? ColumnDefault { get; init; }
    public int OrdinalPosition { get; init; }
}

public sealed class RelationshipDto
{
    public string ConstraintName { get; init; } = string.Empty;
    public string TableName { get; init; } = string.Empty;
    public string ColumnName { get; init; } = string.Empty;
    public string ForeignTableName { get; init; } = string.Empty;
    public string ForeignColumnName { get; init; } = string.Empty;
}

public sealed class FullSchemaDto
{
    public IEnumerable<TableInfoDto> Tables { get; init; } = [];
    public IEnumerable<ColumnInfoDto> Columns { get; init; } = [];
    public IEnumerable<RelationshipDto> Relationships { get; init; } = [];
}
