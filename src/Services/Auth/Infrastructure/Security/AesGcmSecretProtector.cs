using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Cifra secretos TOTP en reposo con AES-256-GCM.
/// La clave se toma de Mfa:EncryptionKey (base64, 32 bytes). Si no está configurada,
/// se deriva del Jwt:Secret (aceptable en desarrollo; en producción configurar la clave propia).
/// Formato del ciphertext: base64(nonce[12] || ciphertext || tag[16]).
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmSecretProtector(IConfiguration configuration)
    {
        var configured = configuration["Mfa:EncryptionKey"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _key = Convert.FromBase64String(configured);
            if (_key.Length != 32)
                throw new InvalidOperationException("Mfa:EncryptionKey must be 32 bytes (base64).");
        }
        else
        {
            var jwtSecret =
                configuration["Jwt:Secret"]
                ?? throw new InvalidOperationException("Mfa:EncryptionKey or Jwt:Secret must be configured.");
            _key = SHA256.HashData(Encoding.UTF8.GetBytes($"{jwtSecret}:taxvision-mfa"));
        }
    }

    public string Protect(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var output = new byte[NonceSize + cipherBytes.Length + TagSize];
        nonce.CopyTo(output, 0);
        cipherBytes.CopyTo(output, NonceSize);
        tag.CopyTo(output, NonceSize + cipherBytes.Length);
        return Convert.ToBase64String(output);
    }

    public string? Unprotect(string ciphertext)
    {
        try
        {
            var input = Convert.FromBase64String(ciphertext);
            if (input.Length < NonceSize + TagSize)
                return null;

            var nonce = input.AsSpan(0, NonceSize);
            var tag = input.AsSpan(input.Length - TagSize, TagSize);
            var cipherBytes = input.AsSpan(NonceSize, input.Length - NonceSize - TagSize);
            var plainBytes = new byte[cipherBytes.Length];

            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
