using System.Text;
using BuildingBlocks.Results;

namespace TaxVision.Campaigns.Domain;

/// <summary>
/// Teléfono en <b>E.164</b> (agnóstico de país): prefijo <c>+</c>, código de país y hasta 15 dígitos. No se adivina el
/// país: sin <c>+</c> no se puede enrutar de forma segura. Solo se limpian separadores visuales.
/// </summary>
public sealed record PhoneE164
{
    public string Value { get; }

    private PhoneE164(string value) => Value = value;

    public static Result<PhoneE164> Create(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result.Failure<PhoneE164>(new Error("PhoneE164.Required", "Phone number is required."));

        var digits = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed.Where(char.IsDigit))
            digits.Append(ch);

        // E.164: código de país (1-3) + número; total 8..15 dígitos, y no puede empezar en 0.
        if (!trimmed.StartsWith('+') || digits.Length is < 8 or > 15 || digits[0] == '0')
            return Result.Failure<PhoneE164>(new Error("PhoneE164.Invalid", "Phone number is not in E.164 format."));

        return Result.Success(new PhoneE164("+" + digits));
    }

    /// <summary>La forma guardada, o <c>null</c> si está vacío o no es E.164.</summary>
    public static string? Normalize(string? raw)
    {
        var phone = Create(raw);
        return phone.IsSuccess ? phone.Value.Value : null;
    }

    public override string ToString() => Value;
}
