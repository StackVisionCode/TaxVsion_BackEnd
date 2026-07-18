using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Scribe.Domain.ValueObjects;

/// <summary>
/// Identificador de un evento de dominio de otro servicio (ej. "auth.password_reset_requested.v1")
/// que <see cref="EventMappings.EventTemplateMapping"/> resuelve hacia un <see cref="TemplateKey"/>.
/// Mismo formato que TemplateKey pero un tipo distinto — representan conceptos diferentes.
/// </summary>
public sealed partial record EventKey
{
    public const int MaxLength = 200;

    public string Value { get; }

    private EventKey(string value) => Value = value;

    public static Result<EventKey> Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return Result.Failure<EventKey>(new Error("EventKey.Empty", "Event key is required."));

        var normalized = candidate.Trim().ToLowerInvariant();
        if (normalized.Length > MaxLength)
            return Result.Failure<EventKey>(
                new Error("EventKey.Length", $"Event key cannot exceed {MaxLength} characters.")
            );

        if (!FormatRegex().IsMatch(normalized))
            return Result.Failure<EventKey>(
                new Error(
                    "EventKey.Format",
                    "Event key must be lowercase segments separated by '.', '_' or '-' (e.g. 'auth.password_reset_requested.v1')."
                )
            );

        return Result.Success(new EventKey(normalized));
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z0-9]+(?:[._-][a-z0-9]+)*$")]
    private static partial Regex FormatRegex();
}
