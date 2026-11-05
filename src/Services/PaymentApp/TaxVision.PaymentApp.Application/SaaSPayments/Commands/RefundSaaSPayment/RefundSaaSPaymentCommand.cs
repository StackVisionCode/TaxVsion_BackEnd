namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.RefundSaaSPayment;

/// <summary>Reembolso de plataforma, parcial o total, de un pago SaaS de cualquier tenant. La moneda
/// siempre es la del pago original: el cliente nunca la decide. <paramref name="ClientIdempotencyKey"/> es la
/// cabecera <c>Idempotency-Key</c> del admin, si la mandó.</summary>
public sealed record RefundSaaSPaymentCommand(
    Guid SaaSPaymentId,
    long RefundAmountCents,
    string Reason,
    Guid ActorUserId,
    string? ClientIdempotencyKey = null
);
