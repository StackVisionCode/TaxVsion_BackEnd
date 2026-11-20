using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Quién puede abrir una sesión <see cref="SessionSurface.Mobile"/>: el cliente desde la app de millaje y el
/// PlatformAdmin desde la app de soporte. Esa cadena se renueva sin binding de host, así que el staff de oficina
/// (que no tiene app) nunca la recibe. Lista explícita: una app nueva se agrega aquí a propósito.
/// </summary>
public static class MobileSurfacePolicy
{
    public static bool Allows(User user, SessionSurface surface) =>
        surface != SessionSurface.Mobile
        || user.ActorType is UserActorType.CustomerPortal or UserActorType.PlatformAdmin;
}
