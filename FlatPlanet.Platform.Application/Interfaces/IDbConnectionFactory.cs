using System.Data.Common;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IDbConnectionFactory
{
    DbConnection CreateConnection();
}
