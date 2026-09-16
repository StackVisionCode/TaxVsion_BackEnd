namespace TaxVision.Subscription.Application.Seats.Commands.StartSeatCheckout;

/// <summary>Inicia la compra de asientos por HOSTED-CHECKOUT (tenant sin método en archivo): crea la
/// intención, resuelve el total prorrateado server-side y pide a PaymentApp la sesión de checkout, devolviendo
/// la URL de redirect. El camino con método en archivo NO usa esto: va por <c>PurchaseSeatsCommand</c>.</summary>
public sealed record StartSeatCheckoutCommand(
    Guid TenantId,
    string SeatType,
    int Quantity,
    bool AutoRenew,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string Provider,
    string Method,
    Guid RequestedByUserId
);

public sealed record StartSeatCheckoutResponse(
    Guid SeatPurchaseIntentId,
    string CheckoutUrl,
    Guid PaymentId,
    DateTime ExpiresAtUtc
);
