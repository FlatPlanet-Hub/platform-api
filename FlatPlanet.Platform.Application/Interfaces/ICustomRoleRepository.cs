using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface ICustomRoleRepository
{
    Task<IEnumerable<CustomRole>> GetAllActiveAsync();
    Task<CustomRole?> GetByIdAsync(Guid id);
    Task<CustomRole?> GetByNameAsync(string name);
    Task<CustomRole> CreateAsync(CustomRole role);
    Task UpdateAsync(CustomRole role);
    Task<IEnumerable<CustomRole>> GetByUserIdAsync(Guid userId);
    Task AssignToUserAsync(Guid userId, Guid customRoleId, Guid assignedBy);
    Task RevokeFromUserAsync(Guid userId, Guid customRoleId);
    Task RevokeAllFromUserAsync(Guid userId);
    Task SetUserCustomRolesAsync(Guid userId, IEnumerable<Guid> customRoleIds, Guid assignedBy);
}
