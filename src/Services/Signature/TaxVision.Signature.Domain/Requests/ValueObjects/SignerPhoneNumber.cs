using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests.ValueObjects;

/// <summary>
/// Teléfono del firmante en formato pragmático E.164 (<c>+&lt;countrycode&gt;&lt;national&gt;</c>).
/// Necesario para canales de verificación por SMS o WhatsApp. La validación aquí es
/// mínima (evita basura obvia); el gateway concreto valida entrega.
/// </summary>
public sealed record SignerPhoneNumber
{
    public const int MinDigits = 8;
    public const int MaxLength = 20;

    public string Value { get; }

    private SignerPhoneNumber(string value) => Value = value;

    public static Result<SignerPhoneNumber> Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return Result.Failure<SignerPhoneNumber>(
                new Error("Signature.SignerPhone.Empty", "Phone number is required.")
            );

        var normalized = Normalize(candidate);
        if (normalized.Length > MaxLength)
            return Result.Failure<SignerPhoneNumber>(
                new Error("Signature.SignerPhone.Length", $"Phone cannot exceed {MaxLength} characters.")
            );

        if (!normalized.StartsWith('+'))
            return Result.Failure<SignerPhoneNumber>(
                new Error("Signature.SignerPhone.Format", "Phone must start with '+' followed by country code.")
            );

        var digitCount = CountDigits(normalized);
        if (digitCount < MinDigits)
            return Result.Failure<SignerPhoneNumber>(
                new Error("Signature.SignerPhone.Digits", $"Phone must have at least {MinDigits} digits.")
            );

        return Result.Success(new SignerPhoneNumber(normalized));
    }

    public override string ToString() => Value;

    private static string Normalize(string candidate)
    {
        var trimmed = candidate.Trim();
        var buffer = new System.Text.StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (c == '+' || char.IsDigit(c))
                buffer.Append(c);
        }
        return buffer.ToString();
    }

    private static int CountDigits(string value)
    {
        var count = 0;
        foreach (var c in value)
            if (char.IsDigit(c))
                count++;
        return count;
    }
}
