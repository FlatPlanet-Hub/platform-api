namespace FlatPlanet.Platform.Application.DTOs.Admin;

public sealed class AdminUserDto
{
    public Guid Id { get; init; }
    public long GitHubId { get; init; }
    public string GitHubUsername { get; init; } = string.Empty;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? AvatarUrl { get; init; }
    public bool IsActive { get; init; }
    public IEnumerable<AdminRoleSummaryDto> SystemRoles { get; init; } = [];
    public IEnumerable<AdminProjectMembershipDto> ProjectMemberships { get; init; } = [];
    public DateTime CreatedAt { get; init; }
}

public sealed class AdminRoleSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class AdminProjectMembershipDto
{
    public Guid ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string ProjectRole { get; init; } = string.Empty;
    public string[] Permissions { get; init; } = [];
}
