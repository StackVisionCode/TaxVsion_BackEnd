namespace TaxVision.PaymentApp.Domain.Audit;

/// <summary>Acción auditada sobre un aggregate de PaymentApp. Persistida como string
/// (HasConversion&lt;string&gt;) — el enum solo aporta type-safety en código, a diferencia del
/// <c>Action: string</c> libre que usa <c>SubscriptionAuditLog</c>.</summary>
public enum PaymentAuditAction
{
    SaaSPaymentCreated = 1,
    SaaSPaymentMarkedProcessing = 2,
    SaaSPaymentRequiresAction = 3,
    SaaSPaymentSucceeded = 4,
    SaaSPaymentFailed = 5,
    SaaSPaymentCancelled = 6,
    SaaSPaymentRefundedPartial = 7,
    SaaSPaymentRefundedFull = 8,
    SaaSPaymentChargedBack = 9,
    SaaSPaymentLegalHoldSet = 10,
    SaaSPaymentLegalHoldCleared = 11,
    ProviderCustomerRegistered = 12,
    PaymentMethodAttached = 13,
    PaymentMethodDetached = 14,
    PaymentMethodSetDefault = 15,
}
