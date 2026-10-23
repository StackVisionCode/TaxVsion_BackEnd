using System.Text.Json;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

internal static class FormSensitiveReferenceReader
{
    // Un "sensitiveRef" solo puede vivir en un valor taxId: el engine rechaza objetos en campos de texto.
    internal static void Collect(string answersJson, ISet<Guid> references)
    {
        using var document = JsonDocument.Parse(answersJson);
        Walk(document.RootElement, references);
    }

    private static void Walk(JsonElement element, ISet<Guid> references)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                Walk(item, references);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object)
            return;
        if (
            element.TryGetProperty("sensitiveRef", out var node)
            && node.ValueKind == JsonValueKind.String
            && node.TryGetGuid(out var id)
        )
            references.Add(id);
        foreach (var property in element.EnumerateObject())
            Walk(property.Value, references);
    }
}
