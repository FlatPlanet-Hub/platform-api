using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FlatPlanet.Platform.Application.Common.Options;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Application.Services;
using FlatPlanet.Platform.Infrastructure.Azure;
using FlatPlanet.Platform.Infrastructure.Common;
using FlatPlanet.Platform.Infrastructure.Configuration;
using FlatPlanet.Platform.Infrastructure.ExternalServices;
using FlatPlanet.Platform.Infrastructure.Repositories;

namespace FlatPlanet.Platform.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        // Configuration
        services.Configure<JwtSettings>(opts => configuration.GetSection("Jwt").Bind(opts));
        services.Configure<SupabaseSettings>(opts => configuration.GetSection("Supabase").Bind(opts));
        services.Configure<GitHubSettings>(opts => configuration.GetSection("GitHub").Bind(opts));
        services.Configure<GitHubOptions>(opts => configuration.GetSection("GitHub").Bind(opts));
        services.Configure<EncryptionSettings>(opts => configuration.GetSection("Encryption").Bind(opts));
        services.Configure<SecurityPlatformSettings>(opts =>
            configuration.GetSection("SecurityPlatform").Bind(opts));
        services.Configure<AzureSettings>(opts => configuration.GetSection("Azure").Bind(opts));
        services.Configure<SupabaseStorageSettings>(opts => configuration.GetSection("SupabaseStorage").Bind(opts));
        services.Configure<DataverseSettings>(opts => configuration.GetSection("Dataverse").Bind(opts));
        services.Configure<NetlifySettings>(opts => configuration.GetSection("Netlify").Bind(opts));

        services.AddHttpClient("SupabaseStorage", (sp, client) =>
        {
            var s = sp.GetRequiredService<IOptions<SupabaseStorageSettings>>().Value;
            client.BaseAddress = new Uri(s.StorageUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {s.ServiceRoleKey}");
            client.DefaultRequestHeaders.Add("apikey", s.ServiceRoleKey);
        });

        // Dataverse HTTP clients
        services.AddHttpClient("Dataverse", (sp, client) =>
        {
            var s = sp.GetRequiredService<IOptions<DataverseSettings>>().Value;
            client.BaseAddress = new Uri(s.ApiBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            client.DefaultRequestHeaders.Add("OData-Version", "4.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("Prefer", "odata.include-annotations=*");
            // Authorization header is set per-request in DataverseService (token is cached)
        });
        services.AddHttpClient("DataverseToken");

        services.AddHttpClient("Netlify", (sp, client) =>
        {
            var s = sp.GetRequiredService<IOptions<NetlifySettings>>().Value;
            client.BaseAddress = new Uri(s.ApiBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {s.ApiToken}");
        });

        services.AddMemoryCache();

        // Infrastructure
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddHttpContextAccessor();

        // Repositories (HubApi owns only these two)
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IApiTokenRepository, ApiTokenRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddHostedService<AuditLogCleanupService>();

        // DB proxy (Feature 1)
        services.AddScoped<IDbProxyService, DbProxyService>();

        // GitHub (service token only)
        services.AddScoped<IGitHubRepoService, GitHubRepoService>();

        // Netlify
        services.AddScoped<INetlifyService, NetlifyService>();

        // Security Platform HTTP clients
        services.AddHttpClient("SecurityPlatform", (sp, client) =>
        {
            var s = sp.GetRequiredService<IOptions<SecurityPlatformSettings>>().Value;
            client.BaseAddress = new Uri(s.BaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", s.ServiceToken);
        });
        services.AddHttpClient("SecurityPlatformUser", (sp, client) =>
        {
            var s = sp.GetRequiredService<IOptions<SecurityPlatformSettings>>().Value;
            client.BaseAddress = new Uri(s.BaseUrl.TrimEnd('/') + "/");
            // Authorization header set per-request in SecurityPlatformService.AuthorizeAsync
        });
        services.AddScoped<ISecurityPlatformService, SecurityPlatformService>();

        // Application services
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IProjectMemberService, ProjectMemberService>();
        services.AddScoped<IClaudeConfigService, ClaudeConfigService>();
        services.AddScoped<IApiTokenService, ApiTokenService>();

        // Azure provisioning
        services.AddScoped<IAzureAppServiceProvisioner, AzureAppServiceProvisioner>();
        services.AddScoped<IProvisionAzureService, ProvisionAzureService>();

        // File storage
        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IStorageBucketService, SupabaseStorageBucketService>();
        services.AddScoped<IFileStorageService, SupabaseFileStorageService>();

        // Dataverse
        services.AddScoped<IDataverseService, DataverseService>();

        return services;
    }
}
