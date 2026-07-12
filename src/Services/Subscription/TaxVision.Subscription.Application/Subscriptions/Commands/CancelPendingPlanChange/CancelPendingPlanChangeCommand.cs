namespace TaxVision.Subscription.Application.Subscriptions.Commands.CancelPendingPlanChange;

public sealed record CancelPendingPlanChangeCommand(Guid TenantId, Guid RequestedByUserId);
