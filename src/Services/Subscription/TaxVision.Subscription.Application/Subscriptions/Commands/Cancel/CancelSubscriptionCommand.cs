namespace TaxVision.Subscription.Application.Subscriptions.Commands.Cancel;

public sealed record CancelSubscriptionCommand(Guid TenantId, string Reason, Guid RequestedByUserId);
