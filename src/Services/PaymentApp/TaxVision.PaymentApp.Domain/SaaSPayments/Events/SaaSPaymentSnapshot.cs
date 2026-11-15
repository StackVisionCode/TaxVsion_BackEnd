using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Domain.SaaSPayments.Events;

/// <summary>
/// Foto del pago en el momento de la transición: todo lo que llevan los avisos de resultado. El handler del
/// evento corre después del commit y no relee el agregado (un reembolso o un reintento posterior cambiaría lo publicado).
/// </summary>
public sealed record SaaSPaymentSnapshot(
    Guid SaaSPaymentId,
    Guid TenantId,
    SaaSPaymentType Type,
    PaymentStatus Status,
    Guid TargetAggregateId,
    string IdempotencyKey,
    long AmountCents,
    string Currency,
    PaymentProviderCode ProviderCode,
    string? ExternalChargeReference,
    DateTime? PaidAtUtc,
    string? FailureCode,
    string? FailureReason,
    DateTime? NextRetryAtUtc,
    int AttemptCount,
    bool HasCheckoutSession,
    Guid CreatedBy,
    Guid? OnboardingId,
    Guid? CodeReservationId,
    Guid? CodeReservationPaymentId,
    long? DiscountAmountCents,
    string? PromotionSnapshotHash,
    int? Quantity,
    long? UnitAmountCents,
    string? ConceptCode,
    string? ConceptDisplayName,
    IReadOnlyList<SaaSPaymentLineSnapshot>? Lines = null
);
