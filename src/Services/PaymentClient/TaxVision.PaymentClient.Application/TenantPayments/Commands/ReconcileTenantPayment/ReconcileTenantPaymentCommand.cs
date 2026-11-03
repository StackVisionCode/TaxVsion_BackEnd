namespace TaxVision.PaymentClient.Application.TenantPayments.Commands.ReconcileTenantPayment;

/// <summary>Pregunta al proveedor por un pago atascado y aplica lo que responda (revisión periódica).</summary>
public sealed record ReconcileTenantPaymentCommand(Guid TenantId, Guid TenantPaymentId);

/// <summary>Qué pasó con el pago en esta vuelta (los errores viajan como <c>Result.Failure</c>).</summary>
public enum TenantPaymentReconciliation
{
    /// <summary>Ya no esperaba al proveedor (lo resolvió un webhook u otra vuelta): nada que hacer.</summary>
    AlreadyResolved,

    /// <summary>El proveedor tampoco lo resolvió todavía: se deja como está.</summary>
    StillPending,

    /// <summary>Se aplicó el estado del proveedor.</summary>
    Resolved,
}
