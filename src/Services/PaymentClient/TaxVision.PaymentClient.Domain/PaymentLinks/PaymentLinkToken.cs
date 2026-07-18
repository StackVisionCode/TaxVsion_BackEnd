using System.Security.Cryptography;
using BuildingBlocks.Results;

namespace TaxVision.PaymentClient.Domain.PaymentLinks;

/// <summary>
/// Token opaco de un link mágico — 32 bytes de RNG criptográfico, codificados base64url
/// (URL-safe, sin padding) para que viaje directo en un path segment sin escaping. No es
/// adivinable ni enumerable: es la única prueba de posesión que el taxpayer presenta, no hay
/// JWT en el flujo de checkout.
/// </summary>
public sealed record PaymentLinkToken
{
    private const int ByteLength = 32;

    public string Value { get; }

    private PaymentLinkToken(string value) => Value = value;

    /// <summary>Genera un token nuevo — usado únicamente al crear un <c>PaymentLink</c>.</summary>
    public static PaymentLinkToken Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(ByteLength);
        var base64Url = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new PaymentLinkToken(base64Url);
    }

    /// <summary>Reconstruye el VO desde un valor ya persistido (EF) o desde el path de un
    /// request de checkout entrante — no valida forma más allá de "no vacío", porque un token
    /// con formato inesperado simplemente no matcheará ningún link en el lookup.</summary>
    public static Result<PaymentLinkToken> FromExisting(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<PaymentLinkToken>(new Error("PaymentLinkToken.Empty", "Token is required."));

        return Result.Success(new PaymentLinkToken(value.Trim()));
    }

    public override string ToString() => Value;
}
