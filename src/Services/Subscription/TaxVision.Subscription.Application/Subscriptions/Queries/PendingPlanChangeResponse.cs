namespace TaxVision.Subscription.Application.Subscriptions.Queries;

public sealed record PendingPlanChangeResponse(
    Guid Id,
    string FromPlanCode,
    string ToPlanCode,
    string EffectiveMode,
    DateTime EffectiveAtUtc,
    DateTime RequestedAtUtc
);
