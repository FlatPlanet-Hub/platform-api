using SupabaseProxy.Domain.ValueObjects;

namespace SupabaseProxy.Application.DTOs;

public sealed class GenerateTokenRequest
{
    public string UserId { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public string Schema { get; init; } = string.Empty;
    public string Permissions { get; init; } = string.Empty;
}

public sealed class ReadQueryRequest
{
    public string Sql { get; init; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; init; }
}

public sealed class WriteQueryRequest
{
    public string Sql { get; init; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; init; }
}

public sealed class CreateTableRequest
{
    public string TableName { get; init; } = string.Empty;
    public IList<ColumnDefinition> Columns { get; init; } = [];
    public bool EnableRls { get; init; }
}

public sealed class AlterTableRequest
{
    public string TableName { get; init; } = string.Empty;
    public IList<AlterColumnOperation> Operations { get; init; } = [];
}

public sealed class AlterColumnOperation
{
    public AlterOperationType Type { get; init; }
    public string ColumnName { get; init; } = string.Empty;
    public string? NewColumnName { get; init; }
    public string? DataType { get; init; }
    public bool? Nullable { get; init; }
}

public enum AlterOperationType
{
    AddColumn,
    DropColumn,
    RenameColumn,
    SetNotNull,
    DropNotNull
}
