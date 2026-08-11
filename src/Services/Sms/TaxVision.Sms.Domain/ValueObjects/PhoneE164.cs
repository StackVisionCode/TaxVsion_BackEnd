using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Sms.Domain.ValueObjects;

/// <summary>Número de teléfono en formato E.164 (`+` seguido de 7..15 dígitos, sin ceros a la izquierda
/// del país). Se normaliza quitando espacios/guiones/paréntesis antes de validar.</summary>
public sealed partial record PhoneE164
{
    public string Value { get; }

    private PhoneE164(string value) => Value = value;

    public static Result<PhoneE164> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Failure<PhoneE164>(SmsErrors.InvalidDestination);

        // Normaliza: quita espacios, guiones, puntos y paréntesis; conserva el '+' inicial.
        var normalized = NonDialChars().Replace(raw.Trim(), string.Empty);

        if (!E164().IsMatch(normalized))
            return Result.Failure<PhoneE164>(SmsErrors.InvalidDestination);

        return Result.Success(new PhoneE164(normalized));
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"[\s\-\.\(\)]")]
    private static partial Regex NonDialChars();

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
    private static partial Regex E164();
}
