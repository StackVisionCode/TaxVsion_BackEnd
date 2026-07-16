namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.RefundSaaSPayment;

/// <summary>Reembolso administrativo parcial o total. La moneda siempre es la del pago
/// original — el cliente nunca la decide.</summary>
public sealed record RefundSaaSPaymentCommand(Guid TenantId, Guid SaaSPaymentId, long RefundAmountCents, string Reason, Guid ActorUserId);
