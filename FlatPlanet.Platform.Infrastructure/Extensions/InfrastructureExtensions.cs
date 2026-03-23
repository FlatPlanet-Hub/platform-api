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

        // Feature 1
        services.AddScoped<IDbProxyService, DbProxyService>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IEncryptionService, EncryptionService>();

        // Database connection factory
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();

        // Feature 2 — Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IProjectRoleRepository, ProjectRoleRepository>();
        services.AddScoped<IProjectMemberRepository, ProjectMemberRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IClaudeTokenRepository, ClaudeTokenRepository>();

        // Feature 2 — Services
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IProjectRoleService, ProjectRoleService>();
        services.AddScoped<IProjectMemberService, ProjectMemberService>();
        services.AddScoped<IAuditService, AuditService>();

        // Feature 3 — Repositories
        services.AddScoped<ICustomRoleRepository, CustomRoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();

        // Feature 3 — Services
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IAdminRoleService, AdminRoleService>();

        // Feature 4 — GitHub Repo Service
        services.AddScoped<IGitHubRepoService, GitHubRepoService>();

        // Feature 5 — Claude config
        services.AddScoped<IClaudeConfigService, ClaudeConfigService>();

        // GitHub OAuth HTTP client
        services.AddHttpClient<IGitHubOAuthService, GitHubOAuthService>();

        return services;
    }
}
