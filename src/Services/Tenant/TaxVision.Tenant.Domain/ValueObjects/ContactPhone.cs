using System.Text;
using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>
/// Teléfono de contacto: 10 a 15 dígitos tras quitar el formato. Un número de EE. UU. (10 dígitos, u 11
/// empezando por 1) queda <c>(555) 123-4567</c>; cualquier otro, <c>+&lt;dígitos&gt;</c>. El código de
/// error lo pone el campo que lo usa (perfil o cumplimiento).
/// </summary>
public sealed record ContactPhone
{
    public const string InvalidMessage =
        "Enter a phone number with 10 to 15 digits, like (555) 123-4567 or +44 20 7946 0958.";

    private const int MinDigits = 10;
    private const int MaxDigits = 15;
    private const int NationalUsDigits = 10;

    public string Value { get; }

    private ContactPhone(string value) => Value = value;

    public static Result<ContactPhone?> CreateOptional(string? raw, string errorCode)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result.Success<ContactPhone?>(null);

        // Solo dígitos y separadores comunes; el + únicamente al inicio.
        var hasPlus = trimmed.StartsWith('+');
        var body = hasPlus ? trimmed[1..] : trimmed;
        var digits = new StringBuilder(body.Length);
        foreach (var c in body)
        {
            if (char.IsAsciiDigit(c))
                digits.Append(c);
            else if (c is not (' ' or '-' or '.' or '(' or ')'))
                return Result.Failure<ContactPhone?>(new Error(errorCode, InvalidMessage));
        }

        if (digits.Length is < MinDigits or > MaxDigits)
            return Result.Failure<ContactPhone?>(new Error(errorCode, InvalidMessage));

        return Result.Success<ContactPhone?>(new(Format(digits.ToString(), hasPlus)));
    }

    public override string ToString() => Value;

    private static string Format(string digits, bool hasPlus)
    {
        var national = digits switch
        {
            { Length: NationalUsDigits } when !hasPlus => digits,
            { Length: NationalUsDigits + 1 } when digits[0] == '1' => digits[1..],
            _ => null,
        };

        return national is null ? $"+{digits}" : $"({national[..3]}) {national[3..6]}-{national[6..]}";
    }
}
