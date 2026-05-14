using System.Data.Common;
using Microsoft.Extensions.Options;
using Npgsql;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Infrastructure.Configuration;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(IOptions<SupabaseSettings> settings)
        => _connectionString = settings.Value.BuildConnectionString();

    [Obsolete("Use CreateConnectionAsync(CancellationToken) instead.")]
    public DbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

    public async Task<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        return conn;
    }
}
