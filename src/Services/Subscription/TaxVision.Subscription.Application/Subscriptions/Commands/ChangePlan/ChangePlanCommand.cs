namespace TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;

public sealed record ChangePlanCommand(Guid TenantId, string PlanCode, Guid RequestedByUserId);
