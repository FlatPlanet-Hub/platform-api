using SupabaseProxy.Domain.Entities;

namespace SupabaseProxy.Application.Interfaces;

public interface IRoleRepository
{
    Task<IEnumerable<Role>> GetAllAsync();
    Task<Role?> GetByNameAsync(string name);
}
