namespace TaxVision.Subscription.Application.Subscriptions.Queries;

public sealed record MySubscriptionResponse(
    string PlanCode,
    string PlanName,
    string Status,
    string BillingCycle,
    decimal MonthlyPriceUsd,
    int MaxUsers,
    int MaxPendingInvitations,
    long StorageQuotaBytes,
    IReadOnlyList<string> EnabledModules,
    DateTime? TrialEndsAtUtc,
    DateTime CurrentPeriodStartUtc,
    DateTime CurrentPeriodEndUtc,
    DateTime? CancelledAtUtc
);
