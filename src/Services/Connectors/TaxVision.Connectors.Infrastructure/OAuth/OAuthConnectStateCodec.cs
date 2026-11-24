using System.Text.Json;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Infrastructure.OAuth;

/// <summary>
/// Cómo se guarda el state en Redis: JSON, para que ningún valor pueda correr los campos. Los valores
/// viejos separados por '|' se siguen leyendo durante el despliegue (viven 10 minutos).
/// </summary>
public static class OAuthConnectStateCodec
{
    private const int LegacyMaxFields = 7;

    public static string Encode(OAuthConnectState state) => JsonSerializer.Serialize(state);

    public static OAuthConnectState? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.TrimStart().StartsWith('{') ? DecodeJson(value) : DecodeLegacy(value);
    }

    private static OAuthConnectState? DecodeJson(string value)
    {
        try
        {
            var state = JsonSerializer.Deserialize<OAuthConnectState>(value);
            return state is null || state.TenantId == Guid.Empty || state.InitiatedByUserId == Guid.Empty
                ? null
                : state;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Formato viejo: tenantId|provider|userId|email|origen|oficina|platformAdmin. Más campos = manipulado.
    private static OAuthConnectState? DecodeLegacy(string value)
    {
        var parts = value.Split('|');
        if (
            parts.Length is < 3 or > LegacyMaxFields
            || !Guid.TryParse(parts[0], out var tenantId)
            || !Enum.TryParse<ProviderCode>(parts[1], out var providerCode)
            || !Guid.TryParse(parts[2], out var initiatedByUserId)
        )
            return null;

        return new OAuthConnectState(
            tenantId,
            providerCode,
            initiatedByUserId,
            parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3] : null,
            parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]) ? parts[4] : null,
            parts.Length >= 6 && parts[5] == "1",
            parts.Length >= 7 && parts[6] == "1"
        );
    }
}
