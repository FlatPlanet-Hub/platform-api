using System.Text.RegularExpressions;
using FlatPlanet.Platform.Application.DTOs.Azure;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Platform.Infrastructure.Azure;

public sealed class ProvisionAzureService(
    IProjectRepository projectRepo,
    IAzureAppServiceProvisioner provisioner,
    IClaudeConfigService claudeConfig,
    ISecurityPlatformService securityPlatform,
    IOptions<JwtSettings> jwtOptions,
    ILogger<ProvisionAzureService> logger) : IProvisionAzureService
{
    private readonly JwtSettings _jwt = jwtOptions.Value;

    public async Task<ProvisionAzureResponse> ProvisionAsync(
        Guid projectId,
        Guid userId,
        string userEmail,
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

        // 4. Authorization check: user must have write/manage_members/owner on the project's app,
        //    or have view_all_projects on dashboard-hub.
        var appAccess = await securityPlatform.GetUserAppAccessAsync(userId);

        var canViewAll = appAccess.Any(a =>
            a.AppSlug.Equals("dashboard-hub", StringComparison.OrdinalIgnoreCase) &&
            a.Permissions.Contains("view_all_projects", StringComparer.OrdinalIgnoreCase));

        if (!canViewAll)
        {
            var projectAccess = appAccess.FirstOrDefault(a => a.AppId == project.AppId.Value);
            var hasPermission = projectAccess is not null &&
                projectAccess.Permissions.Any(p =>
                    p.Equals("write", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("manage_members", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("owner", StringComparison.OrdinalIgnoreCase));

            if (!hasPermission)
                throw new UnauthorizedAccessException("You do not have permission to provision Azure for this project.");
        }

        // 5. Read JWT settings (already loaded via _jwt)

        // 6. Generate API token synchronously so the raw token is available for the App Service env var.
        //    RenderAndStoreTokenAsync generates a raw token, stores the hash in api_tokens,
        //    and returns the rendered CLAUDE-local.md content (we discard the markdown here).
        // Raw tokens are never stored (only hashes) — PlatformApi__Token is set via
        // the raw token returned from RenderAndStoreTokenAsync below.
        var claudeMd = await claudeConfig.RenderAndStoreTokenAsync(project, userId, userEmail, hubBaseUrl);

        // Extract the raw token embedded in the rendered CLAUDE-local.md content.
        // The token line format is: PlatformApi__Token=<rawToken>
        string? platformApiToken = null;
        var tokenMatch = Regex.Match(claudeMd, @"PlatformApi__Token=([^\s\r\n]+)");
        if (tokenMatch.Success)
            platformApiToken = tokenMatch.Groups[1].Value;

        logger.LogInformation(
            "CLAUDE-local.md rendered for project {ProjectId}; token captured: {HasToken}",
            projectId, platformApiToken is not null);

        // 7. Build env vars — use the raw token captured above as PlatformApiToken
        var envVars = new AppServiceEnvVars(
            JwtSecretKey:       _jwt.SecretKey,
            JwtIssuer:          _jwt.Issuer,
            JwtAudience:        _jwt.Audience,
            PlatformApiBaseUrl: hubBaseUrl,
            PlatformApiToken:   platformApiToken,
            SchemaName:         project.SchemaName);

        // 8. Generate a safe App Service name
        var appServiceName = BuildAppServiceName(project.AppSlug ?? project.SchemaName);

        // 9. Provision
        var result = await provisioner.ProvisionAsync(appServiceName, envVars);

        // 10. Persist provisioned state
        project.AzureAppServiceName = result.AppServiceName;
        project.AzureAppServiceUrl  = result.AppServiceUrl;
        project.UpdatedAt           = DateTime.UtcNow;
        await projectRepo.UpdateAsync(project);

        // 11. Return result — surface the raw token so the caller can relay it to the user
        return new ProvisionAzureResponse(result.AppServiceName, result.AppServiceUrl, envVars.PlatformApiToken);
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
        // max 56 chars before -api suffix (total max 60 chars per Azure limit)
        if (name.Length > 60)
            name = name[..56].TrimEnd('-') + "-api";

        // Ensure minimum length of 3
        if (name.Length < 3)
            name = $"fp-{name}";

        return name;
    }
}
