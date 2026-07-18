using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;

/// <summary>Orquesta un cobro completo: crea el <see cref="SaaSPayment"/>, despacha al
/// provider resuelto por <see cref="PaymentProviderCode"/>, y persiste el resultado.
/// Idempotente por <see cref="IdempotencyKey"/> — un segundo comando con la misma key
/// devuelve el pago ya existente sin volver a cobrar.</summary>
public sealed record ChargeSaaSPaymentCommand(
    Guid TenantId,
    string IdempotencyKey,
    long AmountCents,
    string Currency,
    SaaSPaymentType Type,
    Guid TargetAggregateId,
    PaymentProviderCode Provider,
    string PayerEmail,
    string? PayerName,
    Guid RequestedByUserId
);
