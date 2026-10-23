using System.Globalization;
using System.Text.RegularExpressions;

namespace TaxVision.DataCollection.Engine.Formats;

internal static partial class DateFormat
{
    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    // Fecha de calendario `yyyy-MM-dd`, sin depender de la cultura del proceso.
    internal static bool TryParse(string? value, out DateOnly date)
    {
        date = default;
        return value is not null
            && Pattern().IsMatch(value)
            && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
