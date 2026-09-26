namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.RefundSaaSPayment;

/// <summary>Reembolso de plataforma, parcial o total, de un pago SaaS de cualquier tenant. La moneda
/// siempre es la del pago original: el cliente nunca la decide.</summary>
public sealed record RefundSaaSPaymentCommand(
    Guid SaaSPaymentId,
    long RefundAmountCents,
    string Reason,
    Guid ActorUserId
);
