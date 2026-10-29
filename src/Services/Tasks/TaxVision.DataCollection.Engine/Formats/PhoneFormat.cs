using System.Text.RegularExpressions;

namespace TaxVision.DataCollection.Engine.Formats;

public static partial class PhoneFormat
{
    [GeneratedRegex("^\\+[1-9][0-9]{7,14}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string value) => Pattern().IsMatch(value);
}
