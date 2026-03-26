using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Application.Services;
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
        // Configuration
        services.Configure<JwtSettings>(opts => configuration.GetSection("Jwt").Bind(opts));
        services.Configure<SupabaseSettings>(opts => configuration.GetSection("Supabase").Bind(opts));
        services.Configure<GitHubSettings>(opts => configuration.GetSection("GitHub").Bind(opts));
        services.Configure<EncryptionSettings>(opts => configuration.GetSection("Encryption").Bind(opts));

        var spSettings = configuration.GetSection("SecurityPlatform").Get<SecurityPlatformSettings>()
            ?? new SecurityPlatformSettings();
        services.AddHttpClient("SecurityPlatform", client =>
        {
            client.BaseAddress = new Uri(spSettings.BaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", spSettings.ServiceToken);
        });
        services.AddScoped<ISecurityPlatformService, SecurityPlatformService>();

        // Infrastructure
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddScoped<IAuditService, AuditService>();

        // DB Proxy
        services.AddScoped<IDbProxyService, DbProxyService>();

        // GitHub
        services.AddScoped<IGitHubRepoService, GitHubRepoService>();

        // Repositories
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IApiTokenRepository, ApiTokenRepository>();

        // Application services
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IProjectMemberService, ProjectMemberService>();
        services.AddScoped<IClaudeConfigService, ClaudeConfigService>();
        services.AddScoped<IApiTokenService, ApiTokenService>();

        return services;
    }
}
