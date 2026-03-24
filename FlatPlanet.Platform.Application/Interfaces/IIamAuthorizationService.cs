using FlatPlanet.Platform.Application.DTOs.Iam;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IIamAuthorizationService
{
    Task<AuthorizeResponse> AuthorizeAsync(AuthorizeRequest request);
}
