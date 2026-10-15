namespace TaxVision.Subscription.Application.Subscriptions.Commands.CancelRenewalCheckout;

/// <summary>Cancela una SubscriptionRenewalIntent Pending antes de pagar.</summary>
public sealed record CancelRenewalCheckoutCommand(Guid TenantId, Guid IntentId, Guid ActorUserId, string? Reason);
