using SupabaseProxy.Application.DTOs.Auth;

namespace SupabaseProxy.Application.Interfaces;

public interface IGitHubOAuthService
{
    string BuildAuthorizationUrl(string state);
    Task<string> ExchangeCodeForTokenAsync(string code);
    Task<GitHubUserProfile> GetUserProfileAsync(string accessToken);
}
