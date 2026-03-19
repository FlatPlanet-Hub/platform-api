namespace SupabaseProxy.Application.DTOs.Auth;

public sealed class RoleAssignRequest
{
    public Guid UserId { get; init; }
    public string RoleName { get; init; } = string.Empty;
}
