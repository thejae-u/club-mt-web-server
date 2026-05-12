using System.Security.Cryptography;
using System.Text;

namespace ClubMTServer.Services;

public class EncryptionService
{
    private readonly byte[] _key;
    private const int IV_SIZE = 16;

    public EncryptionService(IConfiguration config)
    {
        var keyString = config["ENCRYPTION_KEY"] ?? throw new InvalidOperationException("ENCRYPTION_KEY is missing.");
        
        // Use SHA256 to ensure a robust 256-bit (32 bytes) key derived from the environment variable.
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
    }

    public string? Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor(aes.Key, iv);
        using var ms = new MemoryStream();
        ms.Write(iv, 0, iv.Length);

        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(plainText);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    public string? Decrypt(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText;

        try
        {
            var fullCipher = Convert.FromBase64String(cipherText);
            using var aes = Aes.Create();
            aes.Key = _key;

            var iv = new byte[IV_SIZE];
            Array.Copy(fullCipher, 0, iv, 0, IV_SIZE);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(fullCipher, IV_SIZE, fullCipher.Length - IV_SIZE);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);

            return sr.ReadToEnd();
        }
        catch
        {
            // If decryption fails, it might be unencrypted data or wrong key
            return cipherText;
        }
    }

    public string ComputeHash(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        
        // Use HMAC-SHA256 with the encryption key to prevent rainbow table attacks
        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(input.Trim()));
        return Convert.ToBase64String(hash);
    }
}
