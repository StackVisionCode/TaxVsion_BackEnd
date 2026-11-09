using System.Security.Cryptography;
using System.Text.Json;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.Extensions.Logging;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// NOT-04: el token de un enlace de cuenta. Prefiere <c>ProtectedToken</c> y cae a <c>RawToken</c> solo si falta
/// (Auth solo lo llena si no pudo cifrar). Null si no hay token usable: el correo no sale.
/// </summary>
public sealed class AuthAccountTokenReader(IProtectedPayloadCodec codec, ILogger<AuthAccountTokenReader> logger)
{
    public string? Read(
        ProtectedPayload? protectedToken,
        string? rawToken,
        string schema,
        ProtectedPayloadBinding binding,
        Guid eventId
    )
    {
        if (protectedToken is null)
            return string.IsNullOrEmpty(rawToken) ? null : rawToken;

        try
        {
            return codec.Unprotect<AuthAccountTokenPayload>(protectedToken, schema, binding).Token;
        }
        // Solo lo permanente se trata aquí; un fallo transitorio del anillo de claves sube y Wolverine reintenta.
        catch (Exception ex)
            when (ex is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            // Mientras Auth publique los dos campos, un payload que no abre (p. ej. clave distinta) no corta el correo.
            if (!string.IsNullOrEmpty(rawToken))
            {
                logger.LogWarning(ex, "Event {EventId}: protected token rejected; using the raw token.", eventId);
                return rawToken;
            }

            logger.LogError(ex, "Event {EventId}: protected token rejected; the email is not sent.", eventId);
            return null;
        }
    }
}
