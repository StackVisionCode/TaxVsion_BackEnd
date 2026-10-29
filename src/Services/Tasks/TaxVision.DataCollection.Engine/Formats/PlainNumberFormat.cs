using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaxVision.DataCollection.Engine.Formats;

internal static partial class PlainNumberFormat
{
    private const NumberStyles Strict = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    [GeneratedRegex("^-?[0-9]+(\\.[0-9]+)?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    // Decimal plano invariante: sin separador de miles, exponente, espacios ni signo `+`.
    internal static bool TryParse(string? value, out decimal number)
    {
        number = 0;
        return value is not null
            && Pattern().IsMatch(value)
            && decimal.TryParse(value, Strict, CultureInfo.InvariantCulture, out number);
    }

    // Mismo valor que `Number(value)` en JS para un decimal plano (IEEE 754, redondeo correcto).
    internal static bool TryParse(string? value, out double number)
    {
        number = 0;
        return value is not null
            && Pattern().IsMatch(value)
            && double.TryParse(value, Strict, CultureInfo.InvariantCulture, out number)
            && double.IsFinite(number);
    }

    // Número JSON finito, igual que `typeof v === "number" && Number.isFinite(v)` en JS.
    internal static bool TryRead(JsonElement value, out double number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number) && double.IsFinite(number);
    }

    // Entero JSON (`Number.isInteger` en JS): admite `2.0` y `1e2`, rechaza `2.5`.
    internal static bool TryReadInteger(JsonElement value, long min, long max, out long number)
    {
        number = 0;
        if (
            value.ValueKind != JsonValueKind.Number
            || !value.TryGetDecimal(out var parsed)
            || parsed != decimal.Truncate(parsed)
        )
            return false;
        if (parsed < min || parsed > max)
            return false;
        number = (long)parsed;
        return true;
    }
}
