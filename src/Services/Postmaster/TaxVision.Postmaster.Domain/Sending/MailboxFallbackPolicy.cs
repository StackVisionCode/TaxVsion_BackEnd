using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Domain.Sending;

/// <summary>
/// ¿Se reintenta por el proveedor del sistema cuando falla el buzón de la oficina? Solo si quien pidió
/// el envío lo permite (TenantPreferred), es transaccional (nunca volumen de campaña por la reputación
/// del sistema) y la culpa es del buzón o del proveedor, no del correo ni del destinatario.
/// </summary>
public static class MailboxFallbackPolicy
{
    public static bool ShouldFallBack(ProviderScope requestedScope, EmailStream stream, MailboxSendFailure failure) =>
        requestedScope == ProviderScope.TenantPreferred
        && stream == EmailStream.Transactional
        && failure is MailboxSendFailure.MailboxUnavailable or MailboxSendFailure.ProviderUnavailable;
}
