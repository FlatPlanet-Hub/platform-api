using System.Net.Http.Json;
using System.Text.Json;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class NetlifyService : INetlifyService
{
    private readonly HttpClient _http;
    private readonly ILogger<NetlifyService> _logger;

    public NetlifyService(IHttpClientFactory httpClientFactory, ILogger<NetlifyService> logger)
    {
        _http   = httpClientFactory.CreateClient("Netlify");
        _logger = logger;
    }

    public async Task PushEnvironmentVariableAsync(string siteId, string key, string value)
    {
        // Netlify env var API: PATCH /api/v1/sites/{site_id}/env
        // Body is an array — each item sets one variable across all deploy contexts.
        var payload = new[]
        {
            new
            {
                key,
                values = new[] { new { context = "all", value } }
            }
        };

        var response = await _http.PatchAsJsonAsync($"sites/{siteId}/env", payload);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning(
                "Netlify env push failed for site {SiteId}, key {Key}. Status: {Status}. Body: {Body}",
                siteId, key, response.StatusCode, body);
            return;
        }

        _logger.LogInformation("Netlify env var {Key} pushed to site {SiteId}", key, siteId);
    }

    public async Task TriggerDeployAsync(string siteId)
    {
        // POST /api/v1/sites/{site_id}/builds triggers a new production deploy
        var response = await _http.PostAsync($"sites/{siteId}/builds", null);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning(
                "Netlify deploy trigger failed for site {SiteId}. Status: {Status}. Body: {Body}",
                siteId, response.StatusCode, body);
            return;
        }

        _logger.LogInformation("Netlify deploy triggered for site {SiteId}", siteId);
    }
}
