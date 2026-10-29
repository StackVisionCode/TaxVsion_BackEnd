using System.Text.Json;
using TaxVision.DataCollection.Engine.Formats;
using TaxVision.DataCollection.Engine.Serialization;
using TaxVision.DataCollection.Engine.Text;

namespace TaxVision.DataCollection.Engine.Validation;

internal static class JsonProps
{
    internal static bool IsObject(JsonElement props) =>
        props.ValueKind is JsonValueKind.Object or JsonValueKind.Undefined;

    internal static IEnumerable<JsonProperty> Enumerate(JsonElement props) =>
        props.ValueKind == JsonValueKind.Object ? props.EnumerateObject() : [];

    internal static bool TryGet(JsonElement props, string name, out JsonElement value)
    {
        if (props.ValueKind == JsonValueKind.Object && props.TryGetProperty(name, out value))
            return true;
        value = default;
        return false;
    }

    // Un entero fuera de rango o no entero lo reporta el validador de props; aquí solo se lee.
    internal static int GetInt(JsonElement props, string name, int defaultValue) =>
        TryGet(props, name, out var value)
        && PlainNumberFormat.TryReadInteger(value, int.MinValue, int.MaxValue, out var result)
            ? (int)result
            : defaultValue;

    internal static long GetLong(JsonElement props, string name, long defaultValue) =>
        TryGet(props, name, out var value)
        && PlainNumberFormat.TryReadInteger(value, long.MinValue, long.MaxValue, out var result)
            ? result
            : defaultValue;

    internal static decimal? GetDecimal(JsonElement props, string name)
    {
        if (!TryGet(props, name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String && PlainNumberFormat.TryParse(value.GetString(), out number))
            return number;
        return null;
    }

    internal static HashSet<string> OptionValues(JsonElement props) =>
        TryGet(props, "options", out var options) && options.ValueKind == JsonValueKind.Array
            ? options
                .EnumerateArray()
                .Where(item =>
                    item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("value", out var value)
                    && value.ValueKind == JsonValueKind.String
                )
                .Select(item => item.GetProperty("value").GetString()!)
                .ToHashSet(StringComparer.Ordinal)
            : [];

    internal static string? GetString(JsonElement props, string name) =>
        TryGet(props, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static bool GetBool(JsonElement props, string name, bool defaultValue) =>
        TryGet(props, name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;

    internal static RichTextDocument? GetRichText(JsonElement props, string name)
    {
        if (!TryGet(props, name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        try
        {
            return value.Deserialize<RichTextDocument>(StrictJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
