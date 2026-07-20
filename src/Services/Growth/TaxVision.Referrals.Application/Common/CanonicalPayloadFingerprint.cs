using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TaxVision.Referrals.Application.Common;

/// <summary>
/// SHA-256 canónico con campos length-prefixed. Evita ambigüedad por delimitadores y
/// produce siempre 64 caracteres hexadecimales en minúsculas.
/// </summary>
public static class CanonicalPayloadFingerprint
{
    public static string Compute(params object?[] values)
    {
        var canonical = new StringBuilder();
        foreach (var value in values)
        {
            var text = Format(value);
            canonical.Append(text.Length.ToString(CultureInfo.InvariantCulture));
            canonical.Append(':');
            canonical.Append(text);
            canonical.Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static string Format(object? value) =>
        value switch
        {
            null => "<null>",
            Guid guid => guid.ToString("N"),
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            Enum enumeration => enumeration.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
}
