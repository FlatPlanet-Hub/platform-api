namespace SupabaseProxy.Domain.Entities;

public sealed class ProjectRole
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string[] Permissions { get; set; } = [];
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; init; }
}
