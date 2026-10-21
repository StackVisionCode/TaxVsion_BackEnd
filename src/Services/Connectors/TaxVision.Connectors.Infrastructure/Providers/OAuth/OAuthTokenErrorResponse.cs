using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaxVision.Connectors.Infrastructure.Providers.OAuth;

/// <summary>
/// Cuerpo de error del token endpoint (RFC 6749 §5.2), igual en Google y Microsoft. Qué códigos
/// significan "reconectar" lo decide cada client: los proveedores no los usan igual.
/// </summary>
internal sealed record OAuthTokenErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }

    public static async Task<OAuthTokenErrorResponse?> TryReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<OAuthTokenErrorResponse>(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
