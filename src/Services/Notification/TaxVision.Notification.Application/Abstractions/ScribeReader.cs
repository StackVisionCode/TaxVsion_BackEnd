using System.Text.RegularExpressions;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Quién lee un correo (Fase 5): su idioma, como locale que Scribe acepta ("es", "en-US"), y su zona si no es la
/// de la oficina. Sin zona, Scribe usa la de la oficina; sin oficina, UTC rotulado.
/// </summary>
public sealed partial record ScribeReader(string? Locale, string? TimeZoneId)
{
    /// <summary>Idioma por defecto de la plantilla y zona de la oficina.</summary>
    public static ScribeReader Office { get; } = new(null, null);

    public static ScribeReader From(string? language, string? timeZoneId = null) =>
        new(LocaleOf(language), string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim());

    // Un idioma que no sea un código válido no viaja: Scribe rechazaría el render entero.
    private static string? LocaleOf(string? language)
    {
        var value = language?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;
        if (LocaleCode().IsMatch(value))
            return value;
        return value.ToLowerInvariant() switch
        {
            "spanish" or "español" or "espanol" => "es",
            "english" or "inglés" or "ingles" => "en",
            _ => null,
        };
    }

    [GeneratedRegex("^[a-zA-Z]{2}(-[a-zA-Z]{2})?$")]
    private static partial Regex LocaleCode();
}
