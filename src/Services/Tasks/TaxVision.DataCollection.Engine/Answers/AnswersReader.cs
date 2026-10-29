using System.Text.Json;
using TaxVision.DataCollection.Engine.Serialization;

namespace TaxVision.DataCollection.Engine.Answers;

internal static class AnswersReader
{
    internal static JsonElement Get(IReadOnlyDictionary<string, JsonElement>? values, string key) =>
        values is not null && values.TryGetValue(key, out var value) ? value : default;

    internal static IReadOnlyList<RepeaterRow> ReadRows(FormAnswers answers, string sectionId)
    {
        var value = Get(answers.Values, sectionId);
        if (value.ValueKind != JsonValueKind.Object)
            return [];
        try
        {
            return value.Deserialize<RepeaterRows>(StrictJson.Options)?.Rows ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static bool IsAnswered(JsonElement value, string componentType)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return false;
        return componentType switch
        {
            "text" or "textarea" or "number" or "currency" or "date" or "email" or "phone" or "select" or "radio" =>
                value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()),
            "checkboxGroup" or "file" => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0,
            "yesNo" or "checkbox" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "address" or "taxId" => value.ValueKind == JsonValueKind.Object,
            _ => false,
        };
    }
}
