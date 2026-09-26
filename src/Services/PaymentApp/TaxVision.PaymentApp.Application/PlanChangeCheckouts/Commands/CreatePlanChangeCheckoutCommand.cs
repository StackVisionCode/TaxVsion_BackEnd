using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.PlanChangeCheckouts.Commands;

/// <summary>
/// Crea (o replay idempotente) la sesión de checkout hosteada para pagar un upgrade de plan, cuando el
/// tenant NO tiene método de pago en archivo. Subscription ya resolvió el precio COMPLETO del plan destino,
/// así que acá no se resuelve nada. <see cref="PlanChangeRequestId"/> viaja como <c>TargetAggregateId</c> del
/// pago: es la misma correlación que usa el cobro off-session, así que el resultado cierra el mismo request.
/// </summary>
public sealed record CreatePlanChangeCheckoutCommand(
    Guid TenantId,
    Guid PlanChangeRequestId,
    long AmountCents,
    string Currency,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    PaymentProviderCode Provider = PaymentProviderCode.Stripe,
    PaymentMethodKind Method = PaymentMethodKind.Card
);

public sealed record PlanChangeCheckoutResponse(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);
