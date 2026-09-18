namespace TaxVision.Subscription.Application.Seats.Queries;

public sealed record GetSeatCheckoutStatusQuery(Guid TenantId, Guid IntentId);

/// <summary>Estado de una intención de compra de asientos por checkout. El front lo poll-ea al volver del
/// redirect hasta ver <c>Provisioned</c> (éxito) o <c>Failed</c>.</summary>
public sealed record SeatCheckoutStatusResponse(
    Guid SeatPurchaseIntentId,
    string Status,
    string SeatType,
    int Quantity,
    long ProratedTotalCents,
    string Currency,
    string? CheckoutUrl
);
