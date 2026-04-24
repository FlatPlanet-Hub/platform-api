using System.Text.RegularExpressions;
using FlatPlanet.Platform.Application.DTOs.Azure;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Infrastructure.Configuration;
using FlatPlanet.Platform.Infrastructure.ExternalServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Platform.Infrastructure.Azure;

public sealed class ProvisionAzureService(
    IProjectRepository projectRepo,
    IAzureAppServiceProvisioner provisioner,
    IClaudeConfigService claudeConfig,
    ISecurityPlatformService securityPlatform,
    IGitHubRepoService gitHubRepo,
    INetlifyService netlify,
    IOptions<JwtSettings> jwtOptions,
    ILogger<ProvisionAzureService> logger) : IProvisionAzureService
{
    private readonly JwtSettings _jwt = jwtOptions.Value;

    public async Task<ProvisionAzureResponse> ProvisionAsync(
        Guid projectId,
        Guid userId,
        string userEmail,
        string hubBaseUrl,
        string? appServiceName = null)
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
            (a.RoleName.Equals("platform_owner", StringComparison.OrdinalIgnoreCase) ||
             a.Permissions.Contains("view_all_projects", StringComparer.OrdinalIgnoreCase)));

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

        // 6. Generate API token and rendered CLAUDE-local.md.
        //    RenderAndStoreTokenAsync stores the token hash in api_tokens and returns
        //    both the raw token and the rendered markdown. We use the raw token directly
        //    for PlatformApi__Token — no regex parsing needed.
        var (platformApiToken, _) = await claudeConfig.RenderAndStoreTokenAsync(project, userId, userEmail, hubBaseUrl);

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

        // 8. Resolve App Service name — caller-supplied takes precedence over slug-derived
        appServiceName = string.IsNullOrWhiteSpace(appServiceName)
            ? BuildAppServiceName(project.AppSlug ?? project.SchemaName)
            : BuildAppServiceName(appServiceName); // normalise: lowercase, collapse hyphens, enforce length

        // 9. Provision
        var result = await provisioner.ProvisionAsync(appServiceName, envVars);

        // 10. Persist provisioned state
        project.AzureAppServiceName = result.AppServiceName;
        project.AzureAppServiceUrl  = result.AppServiceUrl;
        project.UpdatedAt           = DateTime.UtcNow;
        await projectRepo.UpdateAsync(project);

        // 11. Push publish profile secret + upgrade workflow to CI/CD (fire-and-forget)
        if (!string.IsNullOrWhiteSpace(result.PublishProfileXml) && !string.IsNullOrWhiteSpace(project.GitHubRepo))
        {
            var cdWorkflow = project.ProjectType.ToLowerInvariant() switch
            {
                "backend" => GitHubRepoService.BuildBackendCdWorkflow(result.AppServiceName),
                _         => GitHubRepoService.BuildFullstackCdWorkflow(result.AppServiceName)
            };

            _ = Task.WhenAll(
                gitHubRepo.SetRepoSecretAsync(project.GitHubRepo, "AZURE_WEBAPP_PUBLISH_PROFILE", result.PublishProfileXml),
                gitHubRepo.UpdateWorkflowAsync(project.GitHubRepo, cdWorkflow)
            ).ContinueWith(t => logger.LogWarning(t.Exception, "Failed to configure GitHub Actions for Azure deployment"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        // 12. Push VITE_API_URL + VITE_PLATFORM_TOKEN to Netlify (fire-and-forget)
        if (!string.IsNullOrWhiteSpace(project.NetlifySiteId))
        {
            _ = Task.WhenAll(
                netlify.PushEnvironmentVariableAsync(project.NetlifySiteId, "VITE_API_URL", result.AppServiceUrl),
                netlify.PushEnvironmentVariableAsync(project.NetlifySiteId, "VITE_PLATFORM_TOKEN", envVars.PlatformApiToken ?? string.Empty),
                netlify.TriggerDeployAsync(project.NetlifySiteId)
            ).ContinueWith(t => logger.LogWarning(t.Exception, "Failed to push env vars to Netlify site {SiteId}", project.NetlifySiteId),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        // 13. Return result — surface the raw token so the caller can relay it to the user
        return new ProvisionAzureResponse(result.AppServiceName, result.AppServiceUrl, envVars.PlatformApiToken);
    }

    public async Task<SyncGitHubActionsResponse> SyncGitHubActionsAsync(
        Guid projectId,
        Guid userId,
        string userEmail)
    {
        // 1. Load project
        var project = await projectRepo.GetByIdAsync(projectId)
            ?? throw new KeyNotFoundException($"Project {projectId} not found.");

        // 2. Guard: must be provisioned
        if (string.IsNullOrWhiteSpace(project.AzureAppServiceName))
            throw new InvalidOperationException("Azure App Service has not been provisioned for this project.");

        // 3. Guard: must have a GitHub repo linked
        if (string.IsNullOrWhiteSpace(project.GitHubRepo))
            throw new InvalidOperationException("Project does not have a GitHub repository linked.");

        // 4. Authorization check: same rules as ProvisionAsync
        var appAccess = await securityPlatform.GetUserAppAccessAsync(userId);

        var canViewAll = appAccess.Any(a =>
            a.AppSlug.Equals("dashboard-hub", StringComparison.OrdinalIgnoreCase) &&
            (a.RoleName.Equals("platform_owner", StringComparison.OrdinalIgnoreCase) ||
             a.Permissions.Contains("view_all_projects", StringComparer.OrdinalIgnoreCase)));

        if (!canViewAll)
        {
            var projectAccess = project.AppId.HasValue
                ? appAccess.FirstOrDefault(a => a.AppId == project.AppId.Value)
                : null;

            var hasPermission = projectAccess is not null &&
                projectAccess.Permissions.Any(p =>
                    p.Equals("write", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("manage_members", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("owner", StringComparison.OrdinalIgnoreCase));

            if (!hasPermission)
                throw new UnauthorizedAccessException("You do not have permission to sync GitHub Actions for this project.");
        }

        // 5. Fetch publish profile from Azure
        var publishProfileXml = await provisioner.GetPublishProfileAsync(project.AzureAppServiceName);

        if (string.IsNullOrWhiteSpace(publishProfileXml))
            throw new InvalidOperationException(
                $"Could not retrieve the publish profile for App Service '{project.AzureAppServiceName}'. Ensure the service exists and the managed identity has access.");

        // 6. Build the appropriate CD workflow
        var cdWorkflow = project.ProjectType.ToLowerInvariant() switch
        {
            "backend" => GitHubRepoService.BuildBackendCdWorkflow(project.AzureAppServiceName),
            _         => GitHubRepoService.BuildFullstackCdWorkflow(project.AzureAppServiceName)
        };

        // 7. Push secret and workflow to GitHub (awaited — not fire-and-forget)
        try
        {
            await Task.WhenAll(
                gitHubRepo.SetRepoSecretAsync(project.GitHubRepo, "AZURE_WEBAPP_PUBLISH_PROFILE", publishProfileXml),
                gitHubRepo.UpdateWorkflowAsync(project.GitHubRepo, cdWorkflow));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"GitHub sync failed for repo '{project.GitHubRepo}': {ex.Message}", ex);
        }

        logger.LogInformation(
            "GitHub Actions synced for project {ProjectId} — repo: {Repo}, app service: {AppService}",
            projectId, project.GitHubRepo, project.AzureAppServiceName);

        return new SyncGitHubActionsResponse(
            project.AzureAppServiceName,
            project.GitHubRepo,
            "GitHub Actions workflow and secret synced successfully.");
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
