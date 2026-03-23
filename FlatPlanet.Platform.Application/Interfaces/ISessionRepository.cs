using FlatPlanet.Platform.Domain.Entities;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface ISessionRepository
{
    Task<Session> CreateAsync(Session session);
    Task<Session?> GetByIdAsync(Guid id);
    Task<IEnumerable<Session>> GetActiveByUserIdAsync(Guid userId);
    Task UpdateLastActiveAsync(Guid id);
    Task EndAsync(Guid id, string reason);
    Task EndAllForUserAsync(Guid userId, string reason);
}
