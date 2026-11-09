namespace TaxVision.PaymentClient.Application.Admin.Commands.RepublishTenantPaymentSucceeded;

/// <summary>
/// Reposición de plataforma: vuelve a publicar la señal "pagado" de los pagos que aprobó el dueño
/// (consulta Q-PCL-2). Por defecto solo simula (<paramref name="DryRun"/>). Billing es idempotente
/// por <c>TenantPaymentId</c> (BIL-1).
/// </summary>
public sealed record RepublishTenantPaymentSucceededCommand(
    IReadOnlyList<Guid> TenantPaymentIds,
    Guid ActorUserId,
    bool DryRun = true
);
