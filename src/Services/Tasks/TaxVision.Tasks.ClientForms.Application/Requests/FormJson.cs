using System.Text.Json;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

internal static class FormJson
{
    // Clona la raíz y libera el documento: sin Dispose, JsonDocument retiene buffers del pool.
    internal static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
