using Microsoft.Extensions.Options;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Infrastructure.Common.Helpers;
using FlatPlanet.Platform.Infrastructure.Configuration;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class EncryptionService : IEncryptionService
{
    private readonly string _key;

    public EncryptionService(IOptions<EncryptionSettings> settings) => _key = settings.Value.Key;

    public string Encrypt(string plaintext) => EncryptionHelper.Encrypt(plaintext, _key);
    public string Decrypt(string ciphertext) => EncryptionHelper.Decrypt(ciphertext, _key);
    public string HashToken(string token) => EncryptionHelper.HashToken(token);
}
