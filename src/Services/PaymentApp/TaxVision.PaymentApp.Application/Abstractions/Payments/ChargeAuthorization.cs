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
/// <paramref name="RefundedAmountCents"/> solo se completa para eventos de refund.
/// <paramref name="ReconciledChargeReference"/> solo se completa cuando el evento trae una
/// referencia de cobro más autoritativa que la usada para resolver el <c>SaaSPayment</c> (p.ej.
/// <c>checkout.session.completed</c> de Stripe, que confirma el PaymentIntent real después de
/// que <see cref="HostedCheckoutSessionResult.ProviderPaymentIntentReference"/> tuvo que caer al
/// id de la Session porque el PaymentIntent no estaba disponible sincrónicamente al crearla).</summary>
public sealed record WebhookEventPayload(
    string ProviderChargeReference,
    PaymentStatus Status,
    string? FailureCode,
    string? FailureMessage,
    long? RefundedAmountCents,
    string? ReconciledChargeReference = null
);

/// <summary>Metadata autoritativa de un método de pago tal como el provider la confirma —
/// nunca lo que el cliente afirma en el request.</summary>
public sealed record SavedPaymentMethodInfo(
    string MethodReference,
    string Brand,
    string Last4,
    int ExpMonth,
    int ExpYear
);

/// <summary>
/// PayFlow (Fase 8) — intención de crear una página de checkout hosteada por el provider
/// (Stripe Checkout Session) para un pago sin customer/tenant preexistente. A diferencia de
/// <see cref="ChargeAuthorizationRequest"/>, no lleva <see cref="ProviderCustomerToken"/>: el
/// provider recolecta los datos del pagador directamente en su propia página.
/// </summary>
public sealed record HostedCheckoutSessionRequest(
    Money Amount,
    IdempotencyKey IdempotencyKey,
    StatementDescriptor Descriptor,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    DateTime ExpiresAtUtc,
    IReadOnlyDictionary<string, string> Metadata
);

/// <summary><paramref name="ProviderPaymentIntentReference"/> es la referencia que el webhook
/// del provider usará después para resolver el <c>SaaSPayment</c> (mismo mecanismo que
/// <see cref="ChargeAuthorizationResult.ProviderChargeReference"/>) — <paramref name="ProviderSessionId"/>
/// es sólo para que el caller pueda mostrar/reusar la página de checkout en un replay idempotente.
/// Stripe no garantiza el PaymentIntent sincrónicamente al crear una Checkout Session en modo
/// <c>payment</c> (<c>payment_intent</c> es <c>nullable</c> por documentación oficial) — cuando eso
/// pasa, <paramref name="ProviderPaymentIntentReference"/> cae al propio
/// <paramref name="ProviderSessionId"/> como referencia provisoria, y se reconcilia con el
/// PaymentIntent real cuando llega el webhook <c>checkout.session.completed</c>.</summary>
public sealed record HostedCheckoutSessionResult(
    string ProviderSessionId,
    string ProviderPaymentIntentReference,
    string CheckoutUrl
);
