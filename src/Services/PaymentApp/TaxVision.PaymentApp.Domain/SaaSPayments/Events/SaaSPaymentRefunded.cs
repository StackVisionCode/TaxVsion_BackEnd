using BuildingBlocks.Domain;

namespace TaxVision.PaymentApp.Domain.SaaSPayments.Events;

/// <summary>
/// Se registró un reembolso (total o parcial) del pago. <paramref name="CumulativeRefundedCents"/> es el total
/// autoritativo ya reembolsado y <paramref name="RefundDeltaCents"/> lo que se movió en esta operación.
/// </summary>
public sealed record SaaSPaymentRefunded(
    SaaSPaymentSnapshot Payment,
    long RefundDeltaCents,
    long CumulativeRefundedCents,
    string Reason,
    DateTime RefundedAtUtc
) : IDomainEvent;

/// <summary>El proveedor reportó un chargeback (contracargo) del pago.</summary>
public sealed record SaaSPaymentChargedBack(SaaSPaymentSnapshot Payment, string Reason, DateTime ChargedBackAtUtc)
    : IDomainEvent;
