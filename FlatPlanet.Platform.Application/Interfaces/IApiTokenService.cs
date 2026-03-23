using FlatPlanet.Platform.Application.DTOs.Iam;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IApiTokenService
{
    Task<ApiTokenResponse> CreateAsync(Guid userId, CreateApiTokenRequest request, string apiBaseUrl);
    Task<IEnumerable<ApiTokenSummaryDto>> ListActiveAsync(Guid userId);
    Task RevokeAsync(Guid tokenId, Guid userId);
}
