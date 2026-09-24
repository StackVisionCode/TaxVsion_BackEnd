using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Profiles;

/// <summary>
/// Regla de visibilidad/uso de firmas personales. Un empleado (no admin) solo puede usar su firma
/// personal si el tenant lo permite (<see cref="TenantSignatureSettings.AllowEmployeeOwnSignature"/>);
/// con el toggle apagado queda limitado a la firma de oficina. Los admins están exentos. Sin settings
/// (tenant sin proyección aún) se asume el default permisivo.
/// </summary>
public static class SignatureVisibilityPolicy
{
    public static bool CanUsePersonal(bool actorIsAdmin, TenantSignatureSettings? settings) =>
        actorIsAdmin || (settings?.AllowEmployeeOwnSignature ?? true);
}
