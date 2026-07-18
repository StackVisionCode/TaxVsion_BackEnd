namespace TaxVision.Subscription.Application.Entitlements.Queries;

public sealed record EntitlementSummaryResponse(
    Guid TenantId,
    long RevisionNumber,
    DateTime ComputedAtUtc,
    string PlanCode,
    string SubscriptionStatus,
    int SeatCount,
    int AvailableSeatCount,
    IReadOnlyDictionary<string, EntitlementValueResponse> Entries
);
