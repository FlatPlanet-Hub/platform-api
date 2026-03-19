using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SupabaseProxy.Application.Interfaces;
using SupabaseProxy.Infrastructure.Configuration;
using SupabaseProxy.Infrastructure.ExternalServices;

namespace SupabaseProxy.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtSettings>(opts => configuration.GetSection("Jwt").Bind(opts));
        services.Configure<SupabaseSettings>(opts => configuration.GetSection("Supabase").Bind(opts));

        services.AddScoped<IDbProxyService, DbProxyService>();
        services.AddScoped<IJwtService, JwtService>();

        return services;
    }
}
