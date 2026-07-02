namespace TaxVision.Customer.Application.Imports.Helpers;

public static class IdentifierNormalizer
{
    /// <summary>Quita guiones, espacios, parentesis. Devuelve solo digitos.</summary>
    public static string NormalizeDigits(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        return new string(raw.Where(char.IsDigit).ToArray());
    }

    public static bool IsValidSsnOrItin(string normalized) =>
        normalized.Length == 9 && !normalized.StartsWith("000") && !normalized.StartsWith("666");

    public static bool IsValidEin(string normalized) => normalized.Length == 9;

    /// <summary>
    /// Convierte un telefono en formato humano a E.164. El import es el boundary que tolera entradas
    /// sucias (paréntesis, guiones, sin codigo pais); el VO PhoneNumber sigue siendo estricto.
    ///
    /// Reglas:
    ///  - Ya empieza con '+' y resto digitos: pasa tal cual.
    ///  - 11 digitos empezando con 1: prepend '+' (formato US/Canada con codigo pais).
    ///  - 10 digitos: prepend '+1' (asume US/Canada por default).
    ///  - Cualquier otra cosa: devuelve string vacio para que el caller decida (error o ignorar).
    /// </summary>
    public static string NormalizePhoneToE164(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('+'))
        {
            var rest = new string(trimmed[1..].Where(char.IsDigit).ToArray());
            return rest.Length is >= 7 and <= 15 ? "+" + rest : string.Empty;
        }

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            11 when digits.StartsWith('1') => "+" + digits,
            10 => "+1" + digits,
            _ => string.Empty,
        };
    }
}
