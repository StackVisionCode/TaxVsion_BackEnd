using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Scribe.Domain.ValueObjects;

/// <summary>
/// Identificador estable de un EmailLayout (ej. "system-base", "tenant-base"). Análogo a
/// <see cref="TemplateKey"/> pero deliberadamente un tipo distinto — un layout y un template no son
/// intercambiables aunque compartan formato de key.
/// </summary>
public sealed partial record LayoutKey
{
    public const int MaxLength = 200;

    public string Value { get; }

    private LayoutKey(string value) => Value = value;

    public static Result<LayoutKey> Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return Result.Failure<LayoutKey>(new Error("LayoutKey.Empty", "Layout key is required."));

        var normalized = candidate.Trim().ToLowerInvariant();
        if (normalized.Length > MaxLength)
            return Result.Failure<LayoutKey>(
                new Error("LayoutKey.Length", $"Layout key cannot exceed {MaxLength} characters.")
            );

        if (!FormatRegex().IsMatch(normalized))
            return Result.Failure<LayoutKey>(
                new Error(
                    "LayoutKey.Format",
                    "Layout key must be lowercase segments separated by '.', '_' or '-' (e.g. 'tenant-base')."
                )
            );

        return Result.Success(new LayoutKey(normalized));
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z0-9]+(?:[._-][a-z0-9]+)*$")]
    private static partial Regex FormatRegex();
}
