using FlatPlanet.Platform.Application.DTOs.Auth;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IGitHubOAuthService
{
    string BuildAuthorizationUrl(string state);
    Task<string> ExchangeCodeForTokenAsync(string code);
    Task<GitHubUserProfile> GetUserProfileAsync(string accessToken);
}
