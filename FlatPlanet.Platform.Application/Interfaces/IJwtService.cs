namespace FlatPlanet.Platform.Application.Interfaces;

public interface IJwtService
{
    string GenerateApiToken(Guid userId, string userName, string userEmail, Guid? appId, string appSlug, string? schema, string[] permissions, int expiryDays, out DateTime expiresAt);
}
