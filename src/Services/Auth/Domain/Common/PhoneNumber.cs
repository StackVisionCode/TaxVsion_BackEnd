using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Common;

/// <summary>
/// Teléfono de contacto en E.164 (`+18095550100`), igual que Customer. Se aceptan espacios, guiones, puntos y
/// paréntesis; sin código de país, 10 dígitos (u 11 empezando por 1) son de +1. Letras: inválido.
/// </summary>
public sealed record PhoneNumber
{
    private static readonly Regex E164 = new(@"^\+[1-9]\d{6,14}$", RegexOptions.Compiled);

    private PhoneNumber(string value) => Value = value;

    public string Value { get; }

    public static Result<PhoneNumber> Create(string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (value.Length == 0 || !value.All(c => char.IsDigit(c) || c is '+' or '-' or ' ' or '.' or '(' or ')'))
            return Invalid();

        var e164 = ToE164(value);
        return e164 is null ? Invalid() : Result.Success(new PhoneNumber(e164));
    }

    /// <summary>Solo dígitos y un `+` inicial; sin código, 10 dígitos son de +1. Null si no queda un E.164 válido.</summary>
    private static string? ToE164(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        var candidate = value.StartsWith('+')
            ? "+" + digits
            : digits.Length switch
            {
                10 => "+1" + digits,
                11 when digits[0] == '1' => "+" + digits,
                _ => null,
            };
        if (candidate is null || !E164.IsMatch(candidate))
            return null;
        // Con +1 el número nacional tiene exactamente 10 dígitos.
        return candidate.StartsWith("+1") && candidate.Length != 12 ? null : candidate;
    }

    private static Result<PhoneNumber> Invalid() =>
        Result.Failure<PhoneNumber>(new Error("User.Phone", "Phone number is invalid."));

    public override string ToString() => Value;
}
