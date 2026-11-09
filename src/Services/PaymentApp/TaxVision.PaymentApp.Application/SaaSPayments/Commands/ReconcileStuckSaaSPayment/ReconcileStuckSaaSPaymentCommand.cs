namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ReconcileStuckSaaSPayment;

/// <summary>Confirma con el provider un pago atascado en Processing. Lo dispara
/// <c>PendingChargeReconciliationJob</c>, un comando por pago: cada uno con su transacción y su outbox.
/// El pago de onboarding viaja con <c>TenantId = Guid.Empty</c>.</summary>
public sealed record ReconcileStuckSaaSPaymentCommand(Guid TenantId, Guid SaaSPaymentId);
