using System.Globalization;
using System.Text.RegularExpressions;

namespace TaxVision.DataCollection.Engine.Formats;

public enum TaxIdentifierKind
{
    Ssn,
    Itin,
    Ein,
}

public static partial class TaxIdentifierFormat
{
    // SSN/EIN sobre corridas de dígitos completas: `[0-9]`, nunca `\d` (dígitos Unicode).
    [GeneratedRegex("(?<![0-9])([0-9]{3}-[0-9]{2}-[0-9]{4}|[0-9]{2}-[0-9]{7})(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex DashedPattern();

    [GeneratedRegex("(?<![0-9])[0-9]{9}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex NineDigitRun();

    public static bool TryNormalize(string value, TaxIdentifierKind kind, out string normalized)
    {
        normalized = new string(value.Where(character => character is not ' ' and not '-').ToArray());
        return normalized.Length == 9 && normalized.All(char.IsAsciiDigit) && IsValidNormalized(normalized, kind);
    }

    public static bool LooksLikeTaxIdentifier(string value) =>
        DashedPattern().IsMatch(value)
        || NineDigitRun()
            .Matches(value)
            .Any(match =>
                IsValidNormalized(match.Value, TaxIdentifierKind.Ssn)
                || IsValidNormalized(match.Value, TaxIdentifierKind.Itin)
                || IsValidNormalized(match.Value, TaxIdentifierKind.Ein)
            );

    private static bool IsValidNormalized(string value, TaxIdentifierKind kind) =>
        kind switch
        {
            TaxIdentifierKind.Ssn => IsValidSsn(value),
            TaxIdentifierKind.Itin => IsValidItin(value),
            TaxIdentifierKind.Ein => value[..2] != "00",
            _ => false,
        };

    private static bool IsValidSsn(string value) =>
        value[..3] is not "000" and not "666"
        && value[0] != '9'
        && value.Substring(3, 2) != "00"
        && value[5..] != "0000";

    private static bool IsValidItin(string value)
    {
        if (
            value[0] != '9'
            || !int.TryParse(value.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var middle)
        )
            return false;
        return middle is >= 50 and <= 65 or >= 70 and <= 88 or >= 90 and <= 92 or >= 94 and <= 99;
    }
}
