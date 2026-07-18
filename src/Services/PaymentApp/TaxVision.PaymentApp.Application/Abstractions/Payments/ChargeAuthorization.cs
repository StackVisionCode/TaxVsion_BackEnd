using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Abstractions.Payments;

/// <summary>
/// Intención de cobro que Application entrega al adapter. <see cref="OnBehalfOf"/> y
/// <see cref="ApplicationFee"/> quedan reservados para PaymentClient (modelo marketplace /
/// Stripe Connect) — PaymentApp siempre los deja en null. Se incluyen aquí, no en un tipo
/// aparte, porque el contrato <see cref="IPaymentProvider"/> se comparte entre ambos bounded
/// contexts (guardrail §44.1 ley 3: agregar PaymentClient no debe tocar este archivo).
/// </summary>
public sealed record ChargeAuthorizationRequest(
    ProviderCustomerToken Customer,
    Money Amount,
    IdempotencyKey IdempotencyKey,
    StatementDescriptor Descriptor,
    IReadOnlyDictionary<string, string> Metadata,
    PaymentMethodToken? SpecificPaymentMethod = null,
    string? OnBehalfOf = null,
    Money? ApplicationFee = null
);

public sealed record ChargeAuthorizationResult(
    string ProviderChargeReference,
    PaymentStatus Status,
    string? NextActionType = null,
    string? NextActionUrl = null,
    string? FailureCode = null,
    string? FailureMessage = null
);

public sealed record CaptureResult(string ProviderChargeReference, PaymentStatus Status, Money CapturedAmount);

public sealed record RefundResult(string ProviderRefundReference, PaymentStatus Status, Money RefundedAmount);

public sealed record WebhookVerificationResult(string ProviderEventId, string EventType, string RawPayload);

/// <summary>Datos canónicos extraídos de un webhook ya verificado — el aggregate afectado
/// (<c>SaaSPayment</c>) los aplica sin conocer el formato del provider.
/// <paramref name="RefundedAmountCents"/> solo se completa para eventos de refund.</summary>
public sealed record WebhookEventPayload(
    string ProviderChargeReference,
    PaymentStatus Status,
    string? FailureCode,
    string? FailureMessage,
    long? RefundedAmountCents);

/// <summary>Metadata autoritativa de un método de pago tal como el provider la confirma —
/// nunca lo que el cliente afirma en el request.</summary>
public sealed record SavedPaymentMethodInfo(string MethodReference, string Brand, string Last4, int ExpMonth, int ExpYear);
