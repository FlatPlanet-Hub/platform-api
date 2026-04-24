namespace FlatPlanet.Platform.Application.Interfaces;

public interface INetlifyService
{
    /// <summary>
    /// Creates or updates a single environment variable on a Netlify site.
    /// </summary>
    Task PushEnvironmentVariableAsync(string siteId, string key, string value);

    /// <summary>
    /// Triggers a new production deploy on the Netlify site so env var changes take effect.
    /// </summary>
    Task TriggerDeployAsync(string siteId);
}
