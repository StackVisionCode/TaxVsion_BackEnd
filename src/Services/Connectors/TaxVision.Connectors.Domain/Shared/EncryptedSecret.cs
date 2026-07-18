using BuildingBlocks.Results;

namespace TaxVision.Connectors.Domain.Shared;

/// <summary>
/// Secreto cifrado en reposo (AES-GCM) con sus componentes expuestos por separado para permitir
/// rotación de master key por fila (<see cref="KeyVersion"/>) — a diferencia del <c>EncryptedSecret</c>
/// de Postmaster (un solo string opaco), acá cada token OAuth necesita su propio nonce/tag porque
/// AccessToken y RefreshToken de un mismo <c>OAuthToken</c> pueden re-cifrarse en momentos distintos
/// (ver Connectors_Service_Design_And_Implementation_Plan.md Fase 3). Nonce de 12 bytes (96 bits) —
/// tamaño estándar recomendado para GCM (NIST SP 800-38D), no los 16 bytes del plan original.
/// </summary>
public sealed class EncryptedSecret
{
    public const int NonceLength = 12;
    public const int TagLength = 16;

    public byte[] Ciphertext { get; private set; } = [];
    public byte[] Nonce { get; private set; } = [];
    public byte[] Tag { get; private set; } = [];
    public short KeyVersion { get; private set; }

    private EncryptedSecret() { }

    private EncryptedSecret(byte[] ciphertext, byte[] nonce, byte[] tag, short keyVersion)
    {
        Ciphertext = ciphertext;
        Nonce = nonce;
        Tag = tag;
        KeyVersion = keyVersion;
    }

    /// <summary>Reconstruye el VO a partir de sus partes crudas (ej: al leer de la base de datos).</summary>
    public static Result<EncryptedSecret> Create(byte[] ciphertext, byte[] nonce, byte[] tag, short keyVersion)
    {
        if (ciphertext is not { Length: > 0 })
            return Result.Failure<EncryptedSecret>(
                new Error("EncryptedSecret.EmptyCiphertext", "Ciphertext cannot be empty.")
            );

        if (nonce is not { Length: NonceLength })
            return Result.Failure<EncryptedSecret>(
                new Error("EncryptedSecret.InvalidNonce", $"Nonce must be {NonceLength} bytes.")
            );

        if (tag is not { Length: TagLength })
            return Result.Failure<EncryptedSecret>(
                new Error("EncryptedSecret.InvalidTag", $"Tag must be {TagLength} bytes.")
            );

        if (keyVersion <= 0)
            return Result.Failure<EncryptedSecret>(
                new Error("EncryptedSecret.InvalidKeyVersion", "KeyVersion must be positive.")
            );

        return Result.Success(new EncryptedSecret(ciphertext, nonce, tag, keyVersion));
    }

    /// <summary>Cifra <paramref name="plaintext"/> vía <paramref name="protector"/> (implementado en Fase 3).</summary>
    public static Result<EncryptedSecret> Create(string plaintext, IEncryptedSecretProtector protector)
    {
        if (string.IsNullOrEmpty(plaintext))
            return Result.Failure<EncryptedSecret>(
                new Error("EncryptedSecret.EmptyPlaintext", "Plaintext cannot be empty.")
            );

        return Result.Success(protector.Protect(plaintext));
    }

    public string Decrypt(IEncryptedSecretProtector protector) => protector.Unprotect(this);

    public override bool Equals(object? obj) =>
        obj is EncryptedSecret other
        && KeyVersion == other.KeyVersion
        && Ciphertext.AsSpan().SequenceEqual(other.Ciphertext)
        && Nonce.AsSpan().SequenceEqual(other.Nonce)
        && Tag.AsSpan().SequenceEqual(other.Tag);

    public override int GetHashCode() => HashCode.Combine(KeyVersion, Convert.ToBase64String(Ciphertext));
}
