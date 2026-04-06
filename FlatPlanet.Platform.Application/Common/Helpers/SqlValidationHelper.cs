using System.Text.RegularExpressions;

namespace FlatPlanet.Platform.Application.Common.Helpers;

public static partial class SqlValidationHelper
{
    private static readonly string[] ReadBlockedKeywords =
        ["drop", "delete", "update", "insert", "alter", "create", "truncate", "grant", "revoke"];

    private static readonly string[] WriteBlockedKeywords =
        ["drop", "alter", "create", "truncate", "grant", "revoke"];

    [GeneratedRegex(@"^project_[a-z0-9][a-z0-9_]{2,62}$")]
    private static partial Regex SchemaNameRegex();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]{0,62}$")]
    private static partial Regex IdentifierRegex();

    public static bool IsValidSchemaName(string schema) =>
        !string.IsNullOrWhiteSpace(schema) && SchemaNameRegex().IsMatch(schema);

    public static bool IsValidIdentifier(string identifier) =>
        !string.IsNullOrWhiteSpace(identifier) && IdentifierRegex().IsMatch(identifier);

    public static (bool isValid, string? error) ValidateReadQuery(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return (false, "SQL cannot be empty.");

        var normalized = sql.ToLowerInvariant();
        foreach (var keyword in ReadBlockedKeywords)
        {
            if (ContainsKeyword(normalized, keyword))
                return (false, $"Keyword '{keyword.ToUpper()}' is not allowed in read queries.");
        }

        return (true, null);
    }

    public static (bool isValid, string? error) ValidateWriteQuery(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return (false, "SQL cannot be empty.");

        var normalized = sql.ToLowerInvariant();
        foreach (var keyword in WriteBlockedKeywords)
        {
            if (ContainsKeyword(normalized, keyword))
                return (false, $"Keyword '{keyword.ToUpper()}' is not allowed in write queries.");
        }

        return (true, null);
    }

    private static bool ContainsKeyword(string sql, string keyword)
    {
        var pattern = $@"\b{Regex.Escape(keyword)}\b";
        return Regex.IsMatch(sql, pattern);
    }
}
