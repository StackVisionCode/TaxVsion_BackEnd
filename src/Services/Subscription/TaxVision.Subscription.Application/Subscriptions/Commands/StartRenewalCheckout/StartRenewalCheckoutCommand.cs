namespace TaxVision.Subscription.Application.Subscriptions.Commands.StartRenewalCheckout;

/// <summary>Inicia la renovación/reactivación self-service de la suscripción base por HOSTED-CHECKOUT
/// (tenant en lapso sin método en archivo): crea la intención, resuelve el monto del ciclo server-side y pide
/// a PaymentApp la sesión de checkout, devolviendo la URL de redirect. Molde: <c>StartSeatCheckoutCommand</c>.</summary>
public sealed record StartRenewalCheckoutCommand(
    Guid TenantId,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string Provider,
    string Method,
    Guid RequestedByUserId,
    // "Pay what's due": los asientos y add-ons elegidos. Null en la renovación del plan solo.
    IReadOnlyList<Guid>? SeatIds = null,
    IReadOnlyList<Guid>? AddOnIds = null
)
{
    public bool PaysDue => SeatIds is not null || AddOnIds is not null;
}

public sealed record StartRenewalCheckoutResponse(
    Guid RenewalIntentId,
    string CheckoutUrl,
    Guid PaymentId,
    DateTime ExpiresAtUtc
);
