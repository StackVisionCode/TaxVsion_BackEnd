using System.Text.Json;
using System.Text.Json.Nodes;

namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Lee <c>emailAddress</c> + <c>historyId</c> del data (ya base64-decodificado) del push de Gmail.
/// Gmail manda <c>historyId</c> como NÚMERO JSON (uint64), no string — deserializarlo directo a un
/// <c>string</c> tira <see cref="JsonException"/> y el webhook rechazaba el push con 400. Leerlo vía
/// <see cref="JsonNode"/> tolera número o string (<c>ToString()</c> normaliza ambos a string).
/// </summary>
public static class GmailPushPayloadParser
{
    public readonly record struct GmailPushPayload(string? EmailAddress, string? HistoryId);

    /// <summary>Devuelve null solo si el JSON no parsea; los campos ausentes quedan en null dentro del payload.</summary>
    public static GmailPushPayload? Parse(string decodedJson)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(decodedJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (node is null)
            return null;

        return new GmailPushPayload(node["emailAddress"]?.ToString(), node["historyId"]?.ToString());
    }
}
