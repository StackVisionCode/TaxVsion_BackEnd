namespace TaxVision.Subscription.Application.Subscriptions.Commands.Suspend;

public sealed record SuspendSubscriptionCommand(Guid TenantId, string Reason, Guid RequestedByUserId);
