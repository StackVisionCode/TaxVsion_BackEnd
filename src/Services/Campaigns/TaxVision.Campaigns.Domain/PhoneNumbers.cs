using System.Text;

namespace TaxVision.Campaigns.Domain;

/// <summary>
/// Normalización/validación de teléfonos a <b>E.164</b> (agnóstico de país). E.164 exige el prefijo
/// internacional <c>+</c> seguido del código de país y hasta 15 dígitos. NO adivinamos el país: un
/// número sin <c>+</c> no se puede enrutar de forma segura (podría ser cualquier país), así que se
/// considera inválido y el llamador debe pedir el formato correcto (p. ej. <c>+18095550142</c>,
/// <c>+34600...</c>). Solo se limpian separadores visuales (espacios, guiones, paréntesis, puntos).
/// </summary>
public static class PhoneNumbers
{
    /// <summary>Devuelve el teléfono normalizado en E.164, o <c>null</c> si está vacío o no es E.164 válido.</summary>
    public static string? ToE164(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith('+'))
            return null; // sin código de país internacional: no enrutéable de forma segura

        var digits = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
            if (char.IsDigit(ch))
                digits.Append(ch);

        var d = digits.ToString();
        // E.164: código de país (1-3) + número; total 8..15 dígitos, y no puede empezar en 0.
        if (d.Length is < 8 or > 15 || d[0] == '0')
            return null;

        return "+" + d;
    }

    /// <summary>True si <paramref name="raw"/> ya es (o normaliza a) un E.164 válido.</summary>
    public static bool IsValidE164(string? raw) => ToE164(raw) is not null;
}
