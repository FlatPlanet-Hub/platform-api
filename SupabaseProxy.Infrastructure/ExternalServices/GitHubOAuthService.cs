using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SupabaseProxy.Application.DTOs.Auth;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Infrastructure.Configuration;

namespace SupabaseProxy.Infrastructure.ExternalServices;

public sealed class GitHubOAuthService : IGitHubOAuthService
{
    private readonly GitHubSettings _settings;
    private readonly HttpClient _httpClient;

    public GitHubOAuthService(IOptions<GitHubSettings> settings, HttpClient httpClient)
    {
        _settings = settings.Value;
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SupabaseProxy/1.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public string BuildAuthorizationUrl(string state)
    {
        var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = _settings.ClientId;
        query["redirect_uri"] = _settings.RedirectUri;
        query["scope"] = "user:email,repo,delete_repo";
        query["state"] = state;
        return $"https://github.com/login/oauth/authorize?{query}";
    }

    public async Task<string> ExchangeCodeForTokenAsync(string code)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "https://github.com/login/oauth/access_token",
            new { client_id = _settings.ClientId, client_secret = _settings.ClientSecret, code, redirect_uri = _settings.RedirectUri });

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GitHubTokenResponse>()
            ?? throw new InvalidOperationException("Failed to exchange code for GitHub access token.");

        if (!string.IsNullOrWhiteSpace(result.Error))
            throw new InvalidOperationException($"GitHub OAuth error: {result.Error} — {result.ErrorDescription}");

        return result.AccessToken;
    }

    public async Task<GitHubUserProfile> GetUserProfileAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var profile = await response.Content.ReadFromJsonAsync<GitHubApiUser>()
            ?? throw new InvalidOperationException("Failed to retrieve GitHub user profile.");

        return new GitHubUserProfile
        {
            Id = profile.Id,
            Login = profile.Login,
            Name = profile.Name,
            Email = profile.Email,
            AvatarUrl = profile.AvatarUrl,
            AccessToken = accessToken
        };
    }

    private sealed class GitHubTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; init; } = string.Empty;
        [JsonPropertyName("error")] public string? Error { get; init; }
        [JsonPropertyName("error_description")] public string? ErrorDescription { get; init; }
    }

    private sealed class GitHubApiUser
    {
        [JsonPropertyName("id")] public long Id { get; init; }
        [JsonPropertyName("login")] public string Login { get; init; } = string.Empty;
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("email")] public string? Email { get; init; }
        [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }
    }
}
