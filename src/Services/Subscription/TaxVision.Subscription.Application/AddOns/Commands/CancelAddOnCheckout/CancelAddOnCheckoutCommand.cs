using TaxVision.Subscription.Application.Seats.Commands.CancelSeatCheckout;

namespace TaxVision.Subscription.Application.AddOns.Commands.CancelAddOnCheckout;

/// <summary>Cancela una AddOnPurchaseIntent Pending antes de pagar.</summary>
public sealed record CancelAddOnCheckoutCommand(Guid TenantId, Guid IntentId, Guid ActorUserId, string? Reason);
