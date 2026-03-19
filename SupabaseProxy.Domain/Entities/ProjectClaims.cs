namespace SupabaseProxy.Domain.Entities;

public sealed class ProjectClaims
{
    public string UserId { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public string Schema { get; init; } = string.Empty;
    public IReadOnlyList<string> Permissions { get; init; } = [];

    public bool HasPermission(string permission) =>
        Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}
