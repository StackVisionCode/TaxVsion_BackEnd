using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Scribe.Domain.ValueObjects;

/// <summary>
/// Locale simplificado estilo RFC 5646 (ej. "es-US", "en-US", "es"). Se normaliza a
/// idioma-en-minúscula[-REGIÓN-EN-MAYÚSCULA] para que la resolución de EventTemplateMapping compare
/// valores consistentes sin importar cómo lo haya tipeado el llamador.
/// </summary>
public sealed partial record Locale
{
    public const int MaxLength = 10;

    public string Value { get; }

    private Locale(string value) => Value = value;

    public static Result<Locale> Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return Result.Failure<Locale>(new Error("Locale.Empty", "Locale is required."));

        var trimmed = candidate.Trim();
        var match = FormatRegex().Match(trimmed);
        if (!match.Success)
            return Result.Failure<Locale>(
                new Error(
                    "Locale.Format",
                    "Locale must be a language code optionally followed by a region (e.g. 'es-US')."
                )
            );

        var language = match.Groups["language"].Value.ToLowerInvariant();
        var region = match.Groups["region"].Success ? "-" + match.Groups["region"].Value.ToUpperInvariant() : "";
        var normalized = language + region;

        if (normalized.Length > MaxLength)
            return Result.Failure<Locale>(new Error("Locale.Length", $"Locale cannot exceed {MaxLength} characters."));

        return Result.Success(new Locale(normalized));
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^(?<language>[a-zA-Z]{2})(-(?<region>[a-zA-Z]{2}))?$")]
    private static partial Regex FormatRegex();
}
