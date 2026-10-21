using System.Text.RegularExpressions;

namespace TaxVision.DataCollection.Engine.Formats;

public static partial class PostalCodeFormat
{
    [GeneratedRegex("^\\d{5}(-\\d{4})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string value) => Pattern().IsMatch(value);
}
