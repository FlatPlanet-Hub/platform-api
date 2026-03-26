namespace FlatPlanet.Platform.Application.DTOs.Project;

public sealed class ProjectResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string SchemaName { get; init; } = string.Empty;
    public Guid OwnerId { get; init; }
    public string? AppSlug { get; init; }
    public string? RoleName { get; init; }
    public string? GitHubRepo { get; init; }
    public string? TechStack { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public IEnumerable<ProjectMemberResponse>? Members { get; init; }
}

public sealed class ProjectMemberResponse
{
    public Guid UserId { get; init; }
    public string GitHubUsername { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public string[] Permissions { get; init; } = [];
    public DateTime GrantedAt { get; init; }
}

public sealed class ProjectRoleResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string[] Permissions { get; init; } = [];
    public bool IsDefault { get; init; }
}
