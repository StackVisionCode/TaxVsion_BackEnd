using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Implementación compartida de <see cref="ISecretProtector"/> con AES-256-GCM.
/// Formato del ciphertext: <c>base64(nonce[12] || ciphertext || tag[16])</c>.
///
/// <para>
/// La clave por defecto es <c>Encryption:MasterKey</c> (base64, exactamente 32 bytes), pero el
/// constructor que recibe los bytes permite que un servicio traiga la suya (BB-10). Eso es lo que
/// hace posible consolidar las copias por-servicio sin re-cifrar nada: Auth sigue usando
/// <c>Mfa:EncryptionKey</c> para sus secretos TOTP, así que los datos ya guardados se siguen
/// descifrando — cambiar de clave los volvería ilegibles.
/// </para>
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private readonly byte[] _key;

    public AesGcmSecretProtector(IConfiguration configuration)
        : this(ResolveKey(configuration, "Encryption:MasterKey")) { }

    public AesGcmSecretProtector(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySize)
            throw new InvalidOperationException($"The master key must be exactly {KeySize} bytes.");

        _key = key;
    }

    /// <summary>Lee y valida una master key base64 de 32 bytes desde la ruta de configuración dada.</summary>
    public static byte[] ResolveKey(IConfiguration configuration, string configurationKey)
    {
        var configured = configuration[configurationKey];
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException($"{configurationKey} must be configured (base64, 32 bytes).");

        var key = Convert.FromBase64String(configured);
        if (key.Length != KeySize)
            throw new InvalidOperationException($"{configurationKey} must be exactly 32 bytes (base64).");

        return key;
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

    public bool TryUnprotect(string? protectedValue, out string plaintext, out SecretUnprotectFailure failure)
    {
        plaintext = string.Empty;

        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            failure = SecretUnprotectFailure.NoValueStored;
            return false;
        }

        byte[] input;
        try
        {
            input = Convert.FromBase64String(protectedValue);
        }
        catch (FormatException)
        {
            failure = SecretUnprotectFailure.MalformedInput;
            return false;
        }

        if (input.Length < NonceSize + TagSize)
        {
            failure = SecretUnprotectFailure.MalformedInput;
            return false;
        }

        var nonce = input.AsSpan(0, NonceSize);
        var tag = input.AsSpan(input.Length - TagSize, TagSize);
        var cipherBytes = input.AsSpan(NonceSize, input.Length - NonceSize - TagSize);
        var plainBytes = new byte[cipherBytes.Length];

        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }
        catch (CryptographicException)
        {
            // El tag no valida: manipulación o clave equivocada, indistinguibles en AES-GCM.
            failure = SecretUnprotectFailure.AuthenticationFailed;
            return false;
        }

        plaintext = Encoding.UTF8.GetString(plainBytes);
        failure = SecretUnprotectFailure.None;
        return true;
    }
}
