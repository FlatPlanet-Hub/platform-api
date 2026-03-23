namespace FlatPlanet.Platform.Application.Interfaces;

public interface IAuditService
{
    Task LogAsync(Guid? userId, Guid? appId, string eventType, string? resource = null, object? details = null, string? ipAddress = null);
    Task<object> QueryAsync(Guid? userId, Guid? appId, string? eventType, DateTime? from, DateTime? to, int page, int pageSize);
}
