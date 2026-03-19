namespace SupabaseProxy.Application.DTOs.Project;

public sealed class CreateProjectRoleRequest
{
    public string Name { get; init; } = string.Empty;
    public string[] Permissions { get; init; } = [];
}

public sealed class UpdateProjectRoleRequest
{
    public string[]? Permissions { get; init; }
}

public sealed class UpdateMemberRoleRequest
{
    public string Role { get; init; } = string.Empty;
}
