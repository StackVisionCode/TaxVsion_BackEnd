using System.Globalization;
using System.Text.Json;
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

    internal static int GetInt(JsonElement props, string name, int defaultValue)
    {
        return TryGet(props, name, out var value) && value.TryGetInt32(out var result) ? result : defaultValue;
    }

    internal static decimal? GetDecimal(JsonElement props, string name)
    {
        if (!TryGet(props, name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number))
            return number;
        return null;
    }

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
