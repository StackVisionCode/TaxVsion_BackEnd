namespace TaxVision.Subscription.Application.Seats.Queries;

public sealed record SeatResponse(
    Guid Id,
    string Type,
    string Status,
    string SourceType,
    Guid? SourceReferenceId,
    DateTime PurchasedAtUtc,
    DateTime? CurrentPeriodStartUtc,
    DateTime? CurrentPeriodEndUtc,
    DateTime? NextRenewalAtUtc,
    bool AutoRenew,
    string BillingCycle,
    Guid? CurrentUserId,
    DateTime? CurrentUserAssignedAtUtc
);
