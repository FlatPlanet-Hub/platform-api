using FlatPlanet.Platform.Application.DTOs.Iam;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IApiTokenService
{
    Task<ApiTokenResponse> CreateAsync(Guid userId, string userName, string userEmail, CreateApiTokenRequest request, string apiBaseUrl, string? ipAddress);
    Task<IEnumerable<ApiTokenSummaryDto>> ListActiveAsync(Guid userId);
    Task RevokeAsync(Guid tokenId, Guid userId, string actorEmail, string? ipAddress);
}
