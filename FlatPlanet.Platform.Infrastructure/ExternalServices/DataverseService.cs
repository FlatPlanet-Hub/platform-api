using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlatPlanet.Platform.Application.DTOs.Dataverse;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class DataverseService : IDataverseService
{
    private const string TokenCacheKey = "dataverse_token";
    private static readonly TimeSpan TokenCacheDuration = TimeSpan.FromMinutes(55);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DataverseSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DataverseService> _logger;

    public DataverseService(
        IHttpClientFactory httpClientFactory,
        IOptions<DataverseSettings> settings,
        IMemoryCache cache,
        ILogger<DataverseService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IEnumerable<EmployeeDto>> GetEmployeesAsync()
    {
        const string query =
            "fp_employees?$select=fp_name,fp_employmentdate,fp_separationdate," +
            "fp_employmentstatus,_fp_activereportingto_value,_fp_activeclient_value" +
            "&$filter=statecode%20eq%200";

        return await QueryDataverseAsync<EmployeeDto>(query);
    }

    public async Task<IEnumerable<AccountDto>> GetAccountsAsync()
    {
        const string query = "accounts?$select=name";

        return await QueryDataverseAsync<AccountDto>(query);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<IEnumerable<T>> QueryDataverseAsync<T>(string relativeQuery)
    {
        var token = await GetTokenAsync();

        // Use the named "Dataverse" client — base address is already set by DI.
        var client = _httpClientFactory.CreateClient("Dataverse");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        // OData headers are set on the named client at registration time, but we
        // re-apply Authorization per-request because the token is dynamic.

        var url = relativeQuery; // relative to BaseAddress set on the named client

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP error calling Dataverse endpoint {Url}", url);
            throw new HttpRequestException($"Failed to call Dataverse endpoint: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "Dataverse returned {StatusCode} for {Url}. Body: {Body}",
                (int)response.StatusCode, url, body);
            throw new HttpRequestException(
                $"Dataverse error: {(int)response.StatusCode} — {response.ReasonPhrase}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<ODataEnvelope<T>>(content, JsonOptions);

        return envelope?.Value ?? [];
    }

    private async Task<string> GetTokenAsync()
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
            return cached;

        var uriBuilder = new UriBuilder(_settings.TokenUrl) { Query = $"code={Uri.EscapeDataString(_settings.TokenFunctionKey)}" };
        var tokenUrl = uriBuilder.Uri.ToString();

        var client = _httpClientFactory.CreateClient("DataverseToken");

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(tokenUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP error fetching Dataverse token from {Url}", tokenUrl);
            throw new HttpRequestException("Failed to fetch Dataverse token.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "Token function returned {StatusCode}. Body: {Body}",
                (int)response.StatusCode, body);
            throw new HttpRequestException(
                $"Dataverse token function error: {(int)response.StatusCode} — {response.ReasonPhrase}");
        }

        var raw = await response.Content.ReadAsStringAsync();
        var token = ExtractToken(raw);

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogError("Dataverse token function returned an empty token. Raw: {Raw}", raw);
            throw new HttpRequestException("Dataverse token function returned an empty token.");
        }

        _cache.Set(TokenCacheKey, token, TokenCacheDuration);
        return token;
    }

    /// <summary>
    /// The Azure Function may return either a plain string token or a JSON
    /// object with a "token" or "access_token" field. Handle both gracefully.
    /// </summary>
    private static string ExtractToken(string raw)
    {
        var trimmed = raw.Trim().Trim('"'); // strip surrounding quotes for plain-string responses

        // If it doesn't look like JSON, treat the whole response as the token.
        if (!raw.TrimStart().StartsWith('{'))
            return trimmed;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("token", out var tokenProp))
                return tokenProp.GetString() ?? string.Empty;

            if (root.TryGetProperty("access_token", out var accessTokenProp))
                return accessTokenProp.GetString() ?? string.Empty;

            if (root.TryGetProperty("accessToken", out var camelProp))
                return camelProp.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            // Malformed JSON — fall through and return the trimmed value.
        }

        return trimmed;
    }

    // OData response envelope
    private sealed record ODataEnvelope<T>(
        [property: JsonPropertyName("value")] IEnumerable<T>? Value);
}
