using System.Text.RegularExpressions;
using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Domain.ValueObjects;

/// <summary>Color de presentación del label, en <c>#RRGGBB</c>.</summary>
public sealed partial record LabelColor
{
    public string Value { get; }

    private LabelColor(string value) => Value = value;

    public static Result<LabelColor> Create(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result.Failure<LabelColor>(TaskErrors.Label.ColorInvalid);

        // Se normaliza a mayúsculas para que #aabbcc y #AABBCC no entren como dos colores distintos.
        var normalized = trimmed.ToUpperInvariant();
        return HexPattern().IsMatch(normalized)
            ? Result.Success(new LabelColor(normalized))
            : Result.Failure<LabelColor>(TaskErrors.Label.ColorInvalid);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^#[0-9A-F]{6}$")]
    private static partial Regex HexPattern();
}
