namespace FlatPlanet.Platform.Domain.ValueObjects;

public sealed class ColumnDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool Nullable { get; init; } = true;
    public bool IsPrimaryKey { get; init; }
    public string? Default { get; init; }
}
