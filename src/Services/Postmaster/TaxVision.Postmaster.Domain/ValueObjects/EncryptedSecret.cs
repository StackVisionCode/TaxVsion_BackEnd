using BuildingBlocks.Results;

namespace TaxVision.Postmaster.Domain.ValueObjects;

/// <summary>
/// Envuelve un secreto ya cifrado en reposo (ej: password SMTP vía <c>ISecretProtector</c>). El
/// dominio nunca ve el valor en claro ni conoce el algoritmo — solo garantiza que nunca se persista
/// una cadena vacía disfrazada de "cifrado".
/// </summary>
public sealed class EncryptedSecret
{
    public string Cipher { get; }

    private EncryptedSecret(string cipher) => Cipher = cipher;

    public static Result<EncryptedSecret> Create(string cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher))
            return Result.Failure<EncryptedSecret>(
                new Error("EncryptedSecret.Empty", "Encrypted secret cannot be empty.")
            );

        return Result.Success(new EncryptedSecret(cipher));
    }

    public override bool Equals(object? obj) => obj is EncryptedSecret other && Cipher == other.Cipher;

    public override int GetHashCode() => Cipher.GetHashCode();
}
