using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Infrastructure.Configuration;
using SupabaseProxy.Infrastructure.ExternalServices;
using SupabaseProxy.Infrastructure.Repositories;

namespace SupabaseProxy.Infrastructure.Extensions;

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

        // Feature 2 — Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IClaudeTokenRepository, ClaudeTokenRepository>();

        // Feature 2 — Services
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IAuditService, AuditService>();

        // Feature 3 — Repositories
        services.AddScoped<ICustomRoleRepository, CustomRoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();

        // Feature 3 — Services
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IAdminRoleService, AdminRoleService>();

        // GitHub OAuth HTTP client
        services.AddHttpClient<IGitHubOAuthService, GitHubOAuthService>();

        return services;
    }
}
