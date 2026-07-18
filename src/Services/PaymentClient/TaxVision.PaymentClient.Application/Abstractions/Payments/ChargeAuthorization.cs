using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>
/// Intención de cobro de un taxpayer que Application entrega al adapter. A diferencia de
/// PaymentApp (que cobra contra un <c>ProviderCustomerToken</c> guardado), acá el método de
/// pago viaja directo — Fase E no modela un customer persistente por tenant/taxpayer.
/// </summary>
/// <summary>
/// <paramref name="OnBehalfOf"/> y <paramref name="ApplicationFee"/> solo se completan en modo
/// <see cref="TaxVision.PaymentClient.Domain.TenantPaymentConfigs.TenantPaymentMode.Connect"/> —
/// el adapter arma un direct charge (header <c>Stripe-Account</c>) que vive en la cuenta del
/// tenant, reteniendo <paramref name="ApplicationFee"/> para la plataforma (§18.4/§19.1 del
/// diseño). En modo DirectApiKeys ambos quedan en null.
/// </summary>
public sealed record ChargeAuthorizationRequest(
    PaymentMethodToken PaymentMethod,
    Money Amount,
    IdempotencyKey IdempotencyKey,
    StatementDescriptor Descriptor,
    string? ReceiptEmail,
    IReadOnlyDictionary<string, string> Metadata,
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

public sealed record RefundResult(string ProviderRefundReference, PaymentStatus Status, Money RefundedAmount);

public sealed record WebhookVerificationResult(string ProviderEventId, string EventType, string RawPayload);

/// <summary>Datos canónicos extraídos de un webhook ya verificado — <c>TenantPayment</c> los
/// aplica sin conocer el formato del provider. <paramref name="RefundedAmountCents"/> solo se
/// completa para eventos de refund.</summary>
public sealed record WebhookEventPayload(
    string ProviderChargeReference,
    PaymentStatus Status,
    string? FailureCode,
    string? FailureMessage,
    long? RefundedAmountCents
);
