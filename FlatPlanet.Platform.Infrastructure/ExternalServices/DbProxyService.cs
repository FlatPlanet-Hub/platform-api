using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Domain.ValueObjects;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class DbProxyService : IDbProxyService
{
    private readonly IDbConnectionFactory _db;

    private static readonly HashSet<string> AllowedColumnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "uuid", "text", "varchar", "char", "boolean", "bool",
        "integer", "int", "int2", "int4", "int8", "bigint", "smallint",
        "numeric", "decimal", "float4", "float8", "real", "double precision",
        "date", "time", "timetz", "timestamp", "timestamptz",
        "jsonb", "json", "bytea", "serial", "bigserial"
    };

    // Whitelisted DEFAULT expressions that are safe to embed in SQL directly.
    private static readonly HashSet<string> AllowedDefaultKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "now()", "current_timestamp", "current_date", "current_time",
        "gen_random_uuid()", "uuid_generate_v4()",
        "true", "false", "null"
    };

    // Matches plain numeric literals: 42, 3.14, -1, -0.5
    private static readonly Regex NumericLiteralRegex =
        new(@"^-?\d+(\.\d+)?$", RegexOptions.Compiled);

    public DbProxyService(IDbConnectionFactory db) => _db = db;

    private static async Task SetSearchPathAsync(System.Data.Common.DbConnection conn, string schema)
    {
        await conn.ExecuteAsync($"SET search_path TO {QuoteIdentifier(schema)}, public");
    }

    private static string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"")}\"";

    public async Task<IEnumerable<TableInfoDto>> GetTablesAsync(string schema)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        const string sql = """
            SELECT table_name AS TableName, table_type AS TableType
            FROM information_schema.tables
            WHERE table_schema = @schema
            ORDER BY table_name
            """;

        return await conn.QueryAsync<TableInfoDto>(sql, new { schema });
    }

    public async Task<IEnumerable<ColumnInfoDto>> GetColumnsAsync(string schema, string? tableName = null)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var sql = """
            SELECT table_name AS TableName, column_name AS ColumnName,
                   data_type AS DataType, is_nullable = 'YES' AS IsNullable,
                   column_default AS ColumnDefault, ordinal_position AS OrdinalPosition
            FROM information_schema.columns
            WHERE table_schema = @schema
            """;

        if (tableName is not null)
            sql += " AND table_name = @tableName";

        sql += " ORDER BY table_name, ordinal_position";

        return await conn.QueryAsync<ColumnInfoDto>(sql, new { schema, tableName });
    }

    public async Task<IEnumerable<RelationshipDto>> GetRelationshipsAsync(string schema)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        const string sql = """
            SELECT
                tc.constraint_name AS ConstraintName,
                tc.table_name AS TableName,
                kcu.column_name AS ColumnName,
                ccu.table_name AS ForeignTableName,
                ccu.column_name AS ForeignColumnName
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
                ON ccu.constraint_name = tc.constraint_name AND ccu.table_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema = @schema
            ORDER BY tc.table_name, kcu.column_name
            """;

        return await conn.QueryAsync<RelationshipDto>(sql, new { schema });
    }

    public async Task<FullSchemaDto> GetFullSchemaAsync(string schema)
    {
        var tables = await GetTablesAsync(schema);
        var columns = await GetColumnsAsync(schema);
        var relationships = await GetRelationshipsAsync(schema);

        return new FullSchemaDto
        {
            Tables = tables,
            Columns = columns,
            Relationships = relationships
        };
    }

    public async Task CreateSchemaAsync(string schema)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync($"CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(schema)}");
    }

    public async Task CreateTableAsync(string schema, CreateTableRequest request)
    {
        var ddl = BuildCreateTableSql(schema, request);
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(ddl);

        if (request.EnableRls)
        {
            var tableFqn = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(request.TableName)}";
            await conn.ExecuteAsync($"ALTER TABLE {tableFqn} ENABLE ROW LEVEL SECURITY");
        }
    }

    public async Task AlterTableAsync(string schema, AlterTableRequest request)
    {
        var statements = BuildAlterTableSql(schema, request);
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        foreach (var stmt in statements)
            await conn.ExecuteAsync(stmt);
    }

    public async Task DropTableAsync(string schema, string tableName)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        var tableFqn = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(tableName)}";
        await conn.ExecuteAsync($"DROP TABLE IF EXISTS {tableFqn}");
    }

    public async Task<IEnumerable<dynamic>> ExecuteReadAsync(string schema, ReadQueryRequest request)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await SetSearchPathAsync(conn, schema);

        var parameters = BuildParameters(request.Parameters);
        return await conn.QueryAsync(request.Sql, parameters);
    }

    public async Task<int> ExecuteWriteAsync(string schema, WriteQueryRequest request)
    {
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        await SetSearchPathAsync(conn, schema);

        var parameters = BuildParameters(request.Parameters);
        return await conn.ExecuteAsync(request.Sql, parameters);
    }

    private static DynamicParameters? BuildParameters(Dictionary<string, JsonElement>? raw)
    {
        if (raw is null) return null;
        var dp = new DynamicParameters();
        foreach (var (key, el) in raw)
            dp.Add(key, UnwrapJsonElement(el));
        return dp;
    }

    private static object? UnwrapJsonElement(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String  => el.GetString(),
            JsonValueKind.Number  => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True    => true,
            JsonValueKind.False   => false,
            JsonValueKind.Null    => null,
            JsonValueKind.Array   => el.EnumerateArray().Select(UnwrapJsonElement).ToArray(),
            _                     => el.ToString()
        };

    private static string BuildCreateTableSql(string schema, CreateTableRequest request)
    {
        var sb = new StringBuilder();
        sb.Append($"CREATE TABLE {QuoteIdentifier(schema)}.{QuoteIdentifier(request.TableName)} (");

        var columnDefs = request.Columns.Select(BuildColumnSql);
        sb.Append(string.Join(", ", columnDefs));
        sb.Append(')');

        return sb.ToString();
    }

    private static string BuildColumnSql(ColumnDefinition col)
    {
        // Validate column type against whitelist (handles parameterised types like varchar(255))
        var baseType = col.Type.Split('(', 2)[0].Trim();
        if (!AllowedColumnTypes.Contains(baseType))
            throw new ArgumentException($"Column type '{col.Type}' is not allowed.");

        var sb = new StringBuilder();
        sb.Append($"{QuoteIdentifier(col.Name)} {col.Type}");

        if (col.IsPrimaryKey)
            sb.Append(" PRIMARY KEY");

        if (!col.Nullable && !col.IsPrimaryKey)
            sb.Append(" NOT NULL");

        if (col.Default is not null)
        {
            if (!IsAllowedDefault(col.Default))
                throw new ArgumentException(
                    $"Default value '{col.Default}' is not allowed. " +
                    "Use a whitelisted SQL function (e.g. now(), gen_random_uuid()), " +
                    "a numeric literal, a boolean, null, or a simple quoted string.");
            sb.Append($" DEFAULT {col.Default}");
        }

        return sb.ToString();
    }

    private static bool IsAllowedDefault(string value)
    {
        var trimmed = value.Trim();

        if (AllowedDefaultKeywords.Contains(trimmed))
            return true;

        if (NumericLiteralRegex.IsMatch(trimmed))
            return true;

        // Simple single-quoted string literal: 'active', 'pending', etc.
        // Inner value must not contain quotes, semicolons, or SQL comment markers.
        if (trimmed.StartsWith('\'') && trimmed.EndsWith('\'') && trimmed.Length >= 2)
        {
            var inner = trimmed[1..^1];
            return !inner.Contains('\'')
                && !inner.Contains(';')
                && !inner.Contains("--")
                && !inner.Contains("/*");
        }

        return false;
    }

    private static IEnumerable<string> BuildAlterTableSql(string schema, AlterTableRequest request)
    {
        var tableFqn = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(request.TableName)}";

        foreach (var op in request.Operations)
        {
            yield return op.Type switch
            {
                AlterOperationType.AddColumn =>
                    $"ALTER TABLE {tableFqn} ADD COLUMN {QuoteIdentifier(op.ColumnName)} {op.DataType}{(op.Nullable == false ? " NOT NULL" : "")}",

                AlterOperationType.DropColumn =>
                    $"ALTER TABLE {tableFqn} DROP COLUMN IF EXISTS {QuoteIdentifier(op.ColumnName)}",

                AlterOperationType.RenameColumn =>
                    $"ALTER TABLE {tableFqn} RENAME COLUMN {QuoteIdentifier(op.ColumnName)} TO {QuoteIdentifier(op.NewColumnName!)}",

                AlterOperationType.SetNotNull =>
                    $"ALTER TABLE {tableFqn} ALTER COLUMN {QuoteIdentifier(op.ColumnName)} SET NOT NULL",

                AlterOperationType.DropNotNull =>
                    $"ALTER TABLE {tableFqn} ALTER COLUMN {QuoteIdentifier(op.ColumnName)} DROP NOT NULL",

                _ => throw new ArgumentOutOfRangeException(nameof(op.Type), $"Unknown operation: {op.Type}")
            };
        }
    }
}
