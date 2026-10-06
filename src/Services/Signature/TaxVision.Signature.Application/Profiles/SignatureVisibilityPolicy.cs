using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Profiles;

/// <summary>
/// F4 — Regla de uso de la firma propia. Dos capas:
/// <list type="number">
///   <item>Kill-switch del tenant: <see cref="TenantSignatureSettings.AllowEmployeeOwnSignature"/>.
///     Si está apagado NADIE puede usar su firma propia, aunque tenga el permiso. Sin settings
///     (tenant sin proyección aún) se asume el default permisivo.</item>
///   <item>Permiso por usuario (RBAC): <c>signature.sign_own</c>. El TenantAdmin lo tiene por
///     bypass (<paramref name="actorIsAdmin"/>). Un empleado necesita el permiso explícito; sin él
///     queda limitado a la firma de oficina.</item>
/// </list>
/// </summary>
public static class SignatureVisibilityPolicy
{
    public static bool CanUsePersonal(bool actorIsAdmin, bool actorHasSignOwn, TenantSignatureSettings? settings)
    {
        if (settings?.AllowEmployeeOwnSignature == false)
            return false;
        return actorIsAdmin || actorHasSignOwn;
    }
}
