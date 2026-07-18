using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Scribe.Domain.ValueObjects;

/// <summary>
/// Identificador estable de un EmailTemplate (ej. "auth.password_reset", "sig.invitation.v1").
/// Es lo que EventTemplateMapping y el render pipeline usan para resolver contenido — nunca el Id
/// interno — así que se normaliza y valida acá para que las comparaciones sean consistentes.
/// </summary>
public sealed partial record TemplateKey
{
    public const int MaxLength = 200;

    public string Value { get; }

    private TemplateKey(string value) => Value = value;

    public static Result<TemplateKey> Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return Result.Failure<TemplateKey>(new Error("TemplateKey.Empty", "Template key is required."));

        var normalized = candidate.Trim().ToLowerInvariant();
        if (normalized.Length > MaxLength)
            return Result.Failure<TemplateKey>(
                new Error("TemplateKey.Length", $"Template key cannot exceed {MaxLength} characters.")
            );

        if (!FormatRegex().IsMatch(normalized))
            return Result.Failure<TemplateKey>(
                new Error(
                    "TemplateKey.Format",
                    "Template key must be lowercase segments separated by '.', '_' or '-' (e.g. 'auth.password_reset')."
                )
            );

        return Result.Success(new TemplateKey(normalized));
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z0-9]+(?:[._-][a-z0-9]+)*$")]
    private static partial Regex FormatRegex();
}
