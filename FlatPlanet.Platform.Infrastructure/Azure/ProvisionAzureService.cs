using System.Text.RegularExpressions;
using FlatPlanet.Platform.Application.DTOs.Azure;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Platform.Infrastructure.Azure;

public sealed class ProvisionAzureService(
    IProjectRepository projectRepo,
    IApiTokenRepository apiTokenRepo,
    IAzureAppServiceProvisioner provisioner,
    IClaudeConfigService claudeConfig,
    IOptions<JwtSettings> jwtOptions,
    ILogger<ProvisionAzureService> logger) : IProvisionAzureService
{
    private readonly JwtSettings _jwt = jwtOptions.Value;

    public async Task<ProvisionAzureResponse> ProvisionAsync(
        Guid projectId,
        Guid userId,
        string hubBaseUrl)
    {
        // 1. Load project
        var project = await projectRepo.GetByIdAsync(projectId)
            ?? throw new KeyNotFoundException($"Project {projectId} not found.");

        // 2. Guard: must be linked to an app
        if (project.AppId is null)
            throw new InvalidOperationException("Project is not linked to a Security Platform app.");

        // 3. Guard: not already provisioned
        if (!string.IsNullOrWhiteSpace(project.AzureAppServiceName))
            throw new InvalidOperationException("Azure App Service is already provisioned for this project.");

        // 4. Fetch most recent active token for this app.
        // Only the token hash is persisted — raw tokens are not stored.
        // PlatformApiToken will be null here; the portal can be used to set it manually.
        await apiTokenRepo.GetActiveByAppIdAsync(project.AppId.Value);
        string? platformApiToken = null;

        // 5. Build env vars
        var envVars = new AppServiceEnvVars(
            JwtSecretKey:       _jwt.SecretKey,
            JwtIssuer:          _jwt.Issuer,
            JwtAudience:        _jwt.Audience,
            PlatformApiBaseUrl: hubBaseUrl,
            PlatformApiToken:   platformApiToken,
            SchemaName:         project.SchemaName);

        // 6. Generate a safe App Service name
        var appServiceName = BuildAppServiceName(project.AppSlug ?? project.SchemaName);

        // 7. Provision
        var result = await provisioner.ProvisionAsync(appServiceName, envVars);

        // 8. Persist provisioned state
        project.AzureAppServiceName = result.AppServiceName;
        project.AzureAppServiceUrl  = result.AppServiceUrl;
        project.UpdatedAt           = DateTime.UtcNow;
        await projectRepo.UpdateAsync(project);

        // 9. Fire-and-forget CLAUDE-local.md regeneration
        _ = Task.Run(async () =>
        {
            try
            {
                await claudeConfig.RenderAndStoreTokenAsync(project, userId, string.Empty, hubBaseUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to regenerate CLAUDE-local.md after Azure provisioning for project {ProjectId}", projectId);
            }
        });

        // 10. Return result
        return new ProvisionAzureResponse(result.AppServiceName, result.AppServiceUrl);
    }

    private static string BuildAppServiceName(string slug)
    {
        // Lowercase, replace non-alphanumeric/non-hyphen with hyphen
        var name = slug.ToLowerInvariant();
        name = Regex.Replace(name, @"[^a-z0-9-]", "-");

        // Collapse consecutive hyphens
        name = Regex.Replace(name, @"-{2,}", "-");

        // Trim leading/trailing hyphens
        name = name.Trim('-');

        // Append -api suffix
        name = $"{name}-api";

        // Enforce max 60 chars (Azure limit is 60 for web app names)
        // The spec says max 55 chars before appending -api, so total max 59
        if (name.Length > 60)
            name = name[..56].TrimEnd('-') + "-api";

        // Ensure minimum length of 3
        if (name.Length < 3)
            name = $"fp-{name}";

        return name;
    }
}
