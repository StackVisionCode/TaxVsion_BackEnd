using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Infrastructure.Security;

/// <summary>
/// Cifrado AES-256-GCM con clave maestra desde configuración (User Secrets en dev,
/// Key Vault en producción). Blind index con HMAC-SHA256 cuya clave se deriva por tenant
/// vía HKDF para que dos tenants con el mismo SSN obtengan blindIndex distintos.
/// </summary>
public sealed class AesGcmSensitiveDataProtector : ISensitiveDataProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32; // 256 bits

    private readonly byte[] _masterKey;

    public AesGcmSensitiveDataProtector(IConfiguration configuration)
    {
        var configured =
            configuration["Encryption:MasterKey"]
            ?? throw new InvalidOperationException("Encryption:MasterKey is missing in configuration.");

        var key = Convert.FromBase64String(configured);
        if (key.Length != KeySize)
            throw new InvalidOperationException(
                $"Encryption:MasterKey must be {KeySize} bytes (base64). Got {key.Length}."
            );

        _masterKey = key;
    }

    public byte[] Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("plainText is empty.", nameof(plainText));

        var plain = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        // Layout: nonce | cipher | tag
        var combined = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, combined, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, combined, NonceSize + cipher.Length, TagSize);
        return combined;
    }

    public string Unprotect(byte[] cipher)
    {
        if (cipher is null || cipher.Length < NonceSize + TagSize)
            throw new ArgumentException("cipher blob is too short.", nameof(cipher));

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertextLength = cipher.Length - NonceSize - TagSize;
        var ciphertext = new byte[ciphertextLength];

        Buffer.BlockCopy(cipher, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(cipher, NonceSize, ciphertext, 0, ciphertextLength);
        Buffer.BlockCopy(cipher, NonceSize + ciphertextLength, tag, 0, TagSize);

        var plain = new byte[ciphertextLength];
        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

    public string ComputeBlindIndex(string plainText, Guid tenantId)
    {
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("plainText is empty.", nameof(plainText));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId is required.", nameof(tenantId));

        // Derivar clave por tenant via HKDF: misma master key, distinto contexto por tenant.
        var info = Encoding.UTF8.GetBytes($"blind-index:tenant:{tenantId:N}");
        var tenantKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: _masterKey,
            outputLength: 32,
            salt: null,
            info: info
        );

        using var hmac = new HMACSHA256(tenantKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(plainText));
        return Convert.ToHexString(hash); // 64 chars
    }
}
