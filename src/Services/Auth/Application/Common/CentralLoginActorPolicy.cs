using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Qué superficies del login central admiten al PlatformAdmin. Hoy ninguna: el CRM y el portal
/// (<see cref="SessionSurface.Workspace"/>) y el Account del Landing son de las oficinas; él opera desde la
/// app de plataforma (<c>POST /auth/login</c>, fuera de este flujo). Una superficie futura que sí lo admita
/// (app de administración, app móvil) se agrega a <see cref="PlatformAdminSurfaces"/> sin tocar el resto.
/// Al rechazarlo se responde el error genérico del paso: nunca se revela que la cuenta es de plataforma.
/// </summary>
public static class CentralLoginActorPolicy
{
    // Vacío a propósito: ninguna superficie de oficina admite al PlatformAdmin.
    private static readonly IReadOnlySet<SessionSurface> PlatformAdminSurfaces = new HashSet<SessionSurface>();

    public static bool IsAllowed(User user, SessionSurface surface) =>
        user.ActorType != UserActorType.PlatformAdmin || PlatformAdminSurfaces.Contains(surface);
}
