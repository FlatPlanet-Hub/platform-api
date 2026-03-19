using System.Security.Cryptography;
using System.Text;

namespace SupabaseProxy.Infrastructure.Common.Helpers;

public static class EncryptionHelper
{
    public static string Encrypt(string plaintext, string key)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey(key);
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public static string Decrypt(string ciphertext, string key)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey(key);

        var data = Convert.FromBase64String(ciphertext);
        var iv = data[..16];
        var cipher = data[16..];
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var result = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(result);
    }

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static byte[] DeriveKey(string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        if (keyBytes.Length == 32) return keyBytes;
        var result = new byte[32];
        Buffer.BlockCopy(keyBytes, 0, result, 0, Math.Min(keyBytes.Length, 32));
        return result;
    }
}
