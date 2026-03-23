namespace FlatPlanet.Platform.Application.Interfaces;

public interface IAuditService
{
    Task LogAsync(Guid? userId, Guid? projectId, string action, string? resource = null, object? details = null, string? ipAddress = null);
}
