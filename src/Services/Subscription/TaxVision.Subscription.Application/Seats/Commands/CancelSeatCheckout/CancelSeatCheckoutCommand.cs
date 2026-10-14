namespace TaxVision.Subscription.Application.Seats.Commands.CancelSeatCheckout;

/// <summary>Cancela una SeatPurchaseIntent Pending antes de pagar (usuario cerró la pasarela, click en "cancelar").</summary>
public sealed record CancelSeatCheckoutCommand(Guid TenantId, Guid IntentId, Guid ActorUserId, string? Reason);

public sealed record CancelCheckoutResponse(bool AlreadyCancelled, Guid IntentId);
