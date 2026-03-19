namespace SupabaseProxy.Application.DTOs.Admin;

public sealed class AdminUserListResponse
{
    public IEnumerable<AdminUserDto> Users { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
