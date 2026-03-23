namespace FlatPlanet.Platform.Application.Interfaces;

public interface IEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
    string HashToken(string token);
}
