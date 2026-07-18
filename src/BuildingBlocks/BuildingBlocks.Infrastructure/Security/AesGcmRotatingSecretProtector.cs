using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Security;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Security;

/// <summary>
/// AES-256-GCM con 2 master keys activas (current + previous) para rotación sin downtime.
/// Nunca loguea plaintext ni ciphertext — no tiene ningún ILogger inyectado a propósito.
/// </summary>
public sealed class AesGcmRotatingSecretProtector : IRotatingSecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly byte[] _currentKey;
    private readonly short _currentKeyVersion;
    private readonly byte[]? _previousKey;
    private readonly short? _previousKeyVersion;

    public AesGcmRotatingSecretProtector(IOptions<RotatingSecretProtectionOptions> options)
    {
        var config = options.Value;

        _currentKey = DecodeKey(config.MasterKey, "Encryption:MasterKey");
        _currentKeyVersion = config.MasterKeyVersion;

        if (!string.IsNullOrWhiteSpace(config.PreviousMasterKey))
        {
            if (config.PreviousMasterKeyVersion is not { } previousVersion)
                throw new InvalidOperationException(
                    "Encryption:PreviousMasterKeyVersion must be set when Encryption:PreviousMasterKey is configured."
                );

            _previousKey = DecodeKey(config.PreviousMasterKey, "Encryption:PreviousMasterKey");
            _previousKeyVersion = previousVersion;
        }
    }

    public RotatingProtectedSecret Protect(string plaintext, short? keyVersion = null)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var (key, version) = ResolveKeyForVersion(keyVersion);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        return new RotatingProtectedSecret(cipherBytes, nonce, tag, version);
    }

    public string Unprotect(RotatingProtectedSecret secret)
    {
        var key = ResolveKeyForDecryption(secret.KeyVersion);

        var plainBytes = new byte[secret.Ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(secret.Nonce, secret.Ciphertext, secret.Tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private (byte[] Key, short Version) ResolveKeyForVersion(short? requestedVersion)
    {
        if (requestedVersion is null || requestedVersion == _currentKeyVersion)
            return (_currentKey, _currentKeyVersion);

        if (requestedVersion == _previousKeyVersion && _previousKey is not null)
            return (_previousKey, _previousKeyVersion!.Value);

        throw new InvalidOperationException($"No master key configured for KeyVersion {requestedVersion}.");
    }

    private byte[] ResolveKeyForDecryption(short keyVersion)
    {
        if (keyVersion == _currentKeyVersion)
            return _currentKey;

        if (keyVersion == _previousKeyVersion && _previousKey is not null)
            return _previousKey;

        throw new CryptographicException($"No master key configured for KeyVersion {keyVersion}.");
    }

    private static byte[] DecodeKey(string configuredValue, string configPath)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
            throw new InvalidOperationException($"{configPath} must be configured (base64, 32 bytes).");

        var key = Convert.FromBase64String(configuredValue);
        if (key.Length != KeySize)
            throw new InvalidOperationException($"{configPath} must be exactly 32 bytes (base64).");

        return key;
    }
}
