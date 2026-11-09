using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Cifra el token de un enlace de cuenta para el campo <c>ProtectedToken</c> de los eventos (NOT-04).
/// Devuelve null si Auth no tiene la clave compartida <c>Encryption:MasterKey</c>: Notification cae a RawToken.
/// </summary>
public interface IAccountTokenProtector
{
    ProtectedPayload? Protect(string schema, ProtectedPayloadBinding binding, string rawToken);
}

/// <summary>Los dos campos del token en un evento: el protegido o, si no se pudo cifrar, el claro.</summary>
public sealed record AccountTokenFields(ProtectedPayload? ProtectedToken, string? RawToken);

public static class AccountTokenProtectorExtensions
{
    // NOT-04 paso 3: el claro solo viaja cuando no hay token protegido.
    public static AccountTokenFields Seal(
        this IAccountTokenProtector protector,
        string schema,
        ProtectedPayloadBinding binding,
        string rawToken
    )
    {
        var protectedToken = protector.Protect(schema, binding, rawToken);
        return new AccountTokenFields(protectedToken, protectedToken is null ? rawToken : null);
    }
}
