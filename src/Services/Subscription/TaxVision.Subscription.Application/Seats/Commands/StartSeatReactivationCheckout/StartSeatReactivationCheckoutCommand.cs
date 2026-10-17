namespace TaxVision.Subscription.Application.Seats.Commands.StartSeatReactivationCheckout;

/// <summary>Inicia el checkout para reactivar un seat Suspended/PastDue específico. El dueño del tenant lo
/// dispara desde el landing al tocar "Pay now" en la fila del seat impago. Al succeed, el consumer de
/// <c>SeatRenewalPaymentSucceeded</c> transiciona el seat a Active con un período NUEVO vía
/// <see cref="TaxVision.Subscription.Domain.Seats.SubscriptionSeat.CompleteReactivation"/>.</summary>
public sealed record StartSeatReactivationCheckoutCommand(
    Guid TenantId,
    Guid SeatId,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string Provider,
    string Method,
    Guid RequestedByUserId
);

public sealed record StartSeatReactivationCheckoutResponse(
    Guid SeatId,
    string CheckoutUrl,
    Guid PaymentId,
    DateTime ExpiresAtUtc
);
