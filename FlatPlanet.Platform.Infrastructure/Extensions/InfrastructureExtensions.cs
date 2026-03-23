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

        // Infrastructure
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddScoped<IAuditService, AuditService>();

        // Feature 1 — DB Proxy
        services.AddScoped<IDbProxyService, DbProxyService>();

        // Feature 2 — Auth repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IProjectRoleRepository, ProjectRoleRepository>();
        services.AddScoped<IProjectMemberRepository, ProjectMemberRepository>();

        // Feature 3 — Admin repositories
        services.AddScoped<ICustomRoleRepository, CustomRoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();

        // Feature 4 — GitHub Repo
        services.AddHttpClient<IGitHubOAuthService, GitHubOAuthService>();
        services.AddScoped<IGitHubRepoService, GitHubRepoService>();

        // Feature 6 — IAM repositories
        services.AddScoped<IApiTokenRepository, ApiTokenRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<IAppRepository, AppRepository>();
        services.AddScoped<IUserAppRoleRepository, UserAppRoleRepository>();
        services.AddScoped<IOAuthProviderRepository, OAuthProviderRepository>();
        services.AddScoped<IUserOAuthLinkRepository, UserOAuthLinkRepository>();
        services.AddScoped<IRolePermissionRepository, RolePermissionRepository>();
        services.AddScoped<IResourceRepository, ResourceRepository>();

        // Application services
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IProjectRoleService, ProjectRoleService>();
        services.AddScoped<IProjectMemberService, ProjectMemberService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IAdminRoleService, AdminRoleService>();

        // Feature 5 — Claude config
        services.AddScoped<IClaudeConfigService, ClaudeConfigService>();

        // Feature 6 — IAM services
        services.AddScoped<IApiTokenService, ApiTokenService>();
        services.AddScoped<IIamAuthorizationService, IamAuthorizationService>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<IAppService, AppService>();
        services.AddScoped<IResourceService, ResourceService>();

        return services;
    }
}
