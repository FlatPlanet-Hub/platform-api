using System.Security.Cryptography;
using System.Text;

namespace FlatPlanet.Platform.Application.Common.Helpers;

public static class TokenHasher
{
    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
