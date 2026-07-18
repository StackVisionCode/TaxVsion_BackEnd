namespace TaxVision.Subscription.Application.AddOns.Queries;

public sealed record AddOnResponse(
    Guid Id,
    string AddOnCode,
    string Status,
    int Quantity,
    string BillingCycle,
    DateTime CurrentPeriodStartUtc,
    DateTime CurrentPeriodEndUtc,
    DateTime? NextRenewalAtUtc,
    bool AutoRenew,
    DateTime PurchasedAtUtc
);
