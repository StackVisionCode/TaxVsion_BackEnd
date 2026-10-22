using System.Globalization;
using System.Text.Json.Nodes;

namespace TaxVision.Postmaster.Infrastructure.Feedback.Smtp2Go;

/// <summary>Lectura tolerante de los campos de SMTP2GO: nombres con cualquier mayúscula, texto o número.</summary>
internal static class Smtp2GoJson
{
    public static string? Field(JsonObject evt, params string[] keys)
    {
        foreach (var key in keys)
        {
            foreach (var (name, value) in evt)
            {
                if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase) || value is null)
                    continue;

                var text =
                    value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var str)
                        ? str
                        : value.ToJsonString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }
        }
        return null;
    }

    /// <summary>SMTP2GO documenta "UTC timestamp" sin formato: se acepta ISO 8601 o segundos Unix.</summary>
    public static DateTime? Time(JsonObject evt, params string[] keys)
    {
        var raw = Field(evt, keys);
        if (raw is null)
            return null;

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;

        return DateTime.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed
        )
            ? parsed
            : null;
    }
}
