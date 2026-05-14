using System.Data.Common;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IDbConnectionFactory
{
    /// <summary>Returns a closed connection. Prefer <see cref="CreateConnectionAsync"/> — this overload has no cancellation support.</summary>
    [Obsolete("Use CreateConnectionAsync(CancellationToken) instead. This method returns a closed connection with no cancellation support.")]
    DbConnection CreateConnection();
    Task<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}
