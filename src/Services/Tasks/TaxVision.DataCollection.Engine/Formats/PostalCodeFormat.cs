using System.Text.RegularExpressions;

namespace TaxVision.DataCollection.Engine.Formats;

public static partial class PostalCodeFormat
{
    [GeneratedRegex("^[0-9]{5}(-[0-9]{4})?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string value) => Pattern().IsMatch(value);
}
