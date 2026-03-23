using FlatPlanet.Platform.Application.DTOs.Iam;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IAppService
{
    Task<AppDto> RegisterAsync(RegisterAppRequest request, Guid registeredBy);
    Task<IEnumerable<AppDto>> ListAsync(Guid? userId = null);
    Task<AppDto?> GetByIdAsync(Guid id);
    Task<AppDto> UpdateAsync(Guid id, UpdateAppRequest request);
    Task UpdateStatusAsync(Guid id, string status);
    Task<IEnumerable<AppUserDto>> GetUsersAsync(Guid appId);
    Task GrantAccessAsync(Guid appId, GrantUserAccessRequest request, Guid grantedBy);
    Task RevokeAccessAsync(Guid appId, Guid userId);
    Task ChangeUserRoleAsync(Guid appId, Guid userId, Guid newRoleId);
}
