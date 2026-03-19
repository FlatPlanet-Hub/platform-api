using SupabaseProxy.Domain.Entities;

namespace SupabaseProxy.Application.Interfaces;

public interface IPermissionRepository
{
    Task<IEnumerable<Permission>> GetAllAsync();
}
