namespace SupabaseProxy.Domain.Entities;

public sealed class ProjectMember
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public Guid UserId { get; init; }
    public Guid ProjectRoleId { get; set; }
    public Guid? InvitedBy { get; init; }
    public string Status { get; set; } = "active";
    public DateTime JoinedAt { get; init; }
}
