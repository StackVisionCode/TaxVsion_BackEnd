using TaxVision.Subscription.Domain.Entitlements;

namespace TaxVision.Subscription.Application.Entitlements.Queries;

public static class EntitlementSummaryMapper
{
    public static EntitlementSummaryResponse ToResponse(TenantEntitlementSnapshot snapshot)
    {
        var entries = new Dictionary<string, EntitlementValueResponse>();
        foreach (var entry in snapshot.Entries)
        {
            entries[entry.Key.Value] = new EntitlementValueResponse(
                entry.ValueType.ToString(), entry.Value, entry.Status.ToString(), entry.Source.ToString(), entry.ExpiresAtUtc);
        }

        return new EntitlementSummaryResponse(
            snapshot.TenantId,
            snapshot.RevisionNumber,
            snapshot.ComputedAtUtc,
            snapshot.PlanCode,
            snapshot.SubscriptionStatus,
            snapshot.SeatCount,
            snapshot.AvailableSeatCount,
            entries
        );
    }
}
