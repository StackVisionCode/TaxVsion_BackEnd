using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Implementación compartida de <see cref="ISecretProtector"/> con AES-256-GCM.
/// La clave se toma de <c>Encryption:MasterKey</c> (base64, exactamente 32 bytes).
/// Formato del ciphertext: <c>base64(nonce[12] || ciphertext || tag[16])</c>.
/// Reemplaza las copias por-servicio (Auth <c>ISecretProtector</c>, Customer
/// <c>ISensitiveDataProtector</c>) para nuevos consumidores.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private readonly byte[] _key;

    public AesGcmSecretProtector(IConfiguration configuration)
    {
        var configured = configuration["Encryption:MasterKey"];
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Encryption:MasterKey must be configured (base64, 32 bytes).");

        _key = Convert.FromBase64String(configured);
        if (_key.Length != KeySize)
            throw new InvalidOperationException("Encryption:MasterKey must be exactly 32 bytes (base64).");
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

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
        if (string.IsNullOrWhiteSpace(ciphertext))
            return null;

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
