using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>Color de marca en formato <c>#RRGGBB</c> (7 caracteres, sin formato corto ni nombres de color).</summary>
public sealed partial record HexColor
{
    public string Value { get; }

    private HexColor(string value) => Value = value;

    public static Result<HexColor> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<HexColor>(new Error("HexColor.Empty", "Hex color is required."));

        var trimmed = value.Trim();

        if (!HexColorPattern().IsMatch(trimmed))
        {
            return Result.Failure<HexColor>(
                new Error("HexColor.InvalidFormat", "Hex color must be in #RRGGBB format, for example #1E466B.")
            );
        }

        return Result.Success(new HexColor(trimmed.ToUpperInvariant()));
    }

    public override string ToString() => Value;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorPattern();
}
