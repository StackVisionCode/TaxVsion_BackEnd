using BuildingBlocks.Results;

namespace TaxVision.PaymentClient.Domain.ValueObjects;

/// <summary>
/// Un secreto de provider (Stripe secret key, webhook signing secret) ya cifrado por
/// <c>ISecretProtector</c> (AES-256-GCM). El dominio nunca ve ni cifra/descifra el
/// plaintext — eso es responsabilidad de Infrastructure/Application, que le entregan a este
/// VO el resultado ya cifrado. Este tipo existe para que sea imposible, a nivel de
/// compilador, guardar un secreto en claro por error donde se espera uno cifrado.
/// </summary>
public sealed record EncryptedSecret
{
    public string CipherText { get; }

    private EncryptedSecret(string cipherText) => CipherText = cipherText;

    public static Result<EncryptedSecret> Create(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
            return Result.Failure<EncryptedSecret>(
                new Error("EncryptedSecret.Empty", "EncryptedSecret value is required.")
            );

        return Result.Success(new EncryptedSecret(cipherText));
    }

    public override string ToString() => "***";
}
