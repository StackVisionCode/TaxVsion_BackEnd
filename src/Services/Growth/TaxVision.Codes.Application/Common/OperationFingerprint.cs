using System.Security.Cryptography;
using System.Text;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Common;

internal static class OperationFingerprint
{
    public static PayloadFingerprint Create(params object?[] values)
    {
        var canonical = new StringBuilder();
        foreach (var value in values)
        {
            var segment = Normalize(value);
            canonical.Append(segment.Length).Append(':').Append(segment).Append('|');
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return PayloadFingerprint.Create(Convert.ToHexStringLower(digest)).Value;
    }

    private static string Normalize(object? value) =>
        value switch
        {
            null => "<null>",
            Guid guid => guid.ToString("N"),
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O"),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O"),
            bool boolean => boolean ? "true" : "false",
            Enum enumeration => Convert
                .ToInt64(enumeration)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
}
