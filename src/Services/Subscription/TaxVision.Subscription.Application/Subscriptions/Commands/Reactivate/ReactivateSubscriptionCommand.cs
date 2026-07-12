namespace TaxVision.Subscription.Application.Subscriptions.Commands.Reactivate;

public sealed record ReactivateSubscriptionCommand(Guid TenantId, Guid RequestedByUserId);
