using System.Text.Json;

namespace TaxVision.DataCollection.Engine.Serialization;

// El modelo no admite `repeat` en un componente (MaxNestingDepth = 1); si el JSON lo trae, el parse
// estricto falla y se reporta repeater.nested en vez de definition.invalid_json (P-1.6).
internal static class NestedRepeaterDetector
{
    private static readonly JsonDocumentOptions Options = new()
    {
        MaxDepth = 32,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    internal static bool HasNestedRepeater(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, Options);
            return Children(document.RootElement, "pages")
                .SelectMany(page => Children(page, "sections"))
                .SelectMany(section => Children(section, "components"))
                .Any(component => component.EnumerateObject().Any(property => property.Name == "repeat"));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IEnumerable<JsonElement> Children(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object)
            return [];
        // Con propiedades duplicadas gana la última, igual que JSON.parse en el runtime TS.
        var value = parent.EnumerateObject().LastOrDefault(property => property.Name == name).Value;
        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];
    }
}
