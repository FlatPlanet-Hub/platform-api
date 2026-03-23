using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IUserAppRoleRepository
{
    Task<UserAppRole> GrantAsync(UserAppRole userAppRole);
    Task<UserAppRole?> GetAsync(Guid userId, Guid appId, Guid roleId);
    Task<IEnumerable<UserAppRole>> GetByUserAndAppAsync(Guid userId, Guid appId);
    Task<IEnumerable<UserAppRole>> GetByAppAsync(Guid appId);
    Task<IEnumerable<UserAppRole>> GetByUserAsync(Guid userId);
    Task UpdateStatusAsync(Guid id, string status);
    Task RevokeAsync(Guid userId, Guid appId);
    Task ChangeRoleAsync(Guid userId, Guid appId, Guid newRoleId);
}
