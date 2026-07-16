using TaxVision.PaymentClient.Domain.Recurring;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Recurring.Commands.CreateTenantRecurringPayment;

/// <summary><see cref="PaymentMethodReference"/> ya viene tokenizado por el frontend (Stripe
/// Elements SetupIntent en modo off-session) — todas las cuotas del plan cobran contra esa
/// misma referencia, no hay flujo de re-tokenización por cuota.</summary>
public sealed record CreateTenantRecurringPaymentCommand(
    Guid TenantId,
    Guid TaxpayerId,
    PaymentProviderCode ProviderCode,
    string PaymentMethodReference,
    long AmountCents,
    string Currency,
    PaymentPurposeKind PurposeKind,
    string? PurposeExternalReferenceId,
    BillingCycle BillingCycle,
    int? CustomIntervalDays,
    DateTime StartDate,
    DateTime? EndDate,
    int? MaxExecutions,
    long? PlatformFeeAmountCents,
    string? PlatformFeeReference,
    Guid ActorUserId
);
