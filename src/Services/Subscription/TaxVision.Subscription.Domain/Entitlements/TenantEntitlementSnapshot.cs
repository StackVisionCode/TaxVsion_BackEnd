using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.Entitlements;

/// <summary>
/// Proyección consolidada y determinista de los derechos vigentes de un tenant (plan +
/// add-ons + overrides + seats). Es la fuente de verdad que consultan el resto de
/// servicios — nunca se edita en el sitio: cada recálculo produce un snapshot nuevo con
/// <see cref="RevisionNumber"/> incrementado, y el repositorio reemplaza la fila anterior.
/// </summary>
public sealed class TenantEntitlementSnapshot
{
    private readonly List<EntitlementEntry> _entries = [];

    public Guid TenantId { get; private set; }
    public long RevisionNumber { get; private set; }
    public DateTime ComputedAtUtc { get; private set; }
    public string PlanCode { get; private set; } = default!;
    public Guid PlanVersionId { get; private set; }
    public string SubscriptionStatus { get; private set; } = default!;
    public int SeatCount { get; private set; }
    public int AvailableSeatCount { get; private set; }

    public IReadOnlyCollection<EntitlementEntry> Entries => _entries;

    private TenantEntitlementSnapshot() { }

    public static Result<TenantEntitlementSnapshot> Rebuild(
        Guid tenantId,
        long previousRevisionNumber,
        string planCode,
        Guid planVersionId,
        string subscriptionStatus,
        int seatCount,
        int availableSeatCount,
        IReadOnlyCollection<EntitlementEntry> entries,
        DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantEntitlementSnapshot>(new Error("EntitlementSnapshot.InvalidTenant", "TenantId is required."));

        if (seatCount < 0 || availableSeatCount < 0)
        {
            return Result.Failure<TenantEntitlementSnapshot>(
                new Error("EntitlementSnapshot.InvalidSeatCount", "Seat counts cannot be negative."));
        }

        var snapshot = new TenantEntitlementSnapshot
        {
            TenantId = tenantId,
            RevisionNumber = previousRevisionNumber + 1,
            ComputedAtUtc = nowUtc,
            PlanCode = planCode,
            PlanVersionId = planVersionId,
            SubscriptionStatus = subscriptionStatus,
            SeatCount = seatCount,
            AvailableSeatCount = availableSeatCount,
        };
        snapshot._entries.AddRange(entries);
        return Result.Success(snapshot);
    }

    public EntitlementEntry? FindByKey(string key)
    {
        foreach (var entry in _entries)
        {
            if (entry.Key.Value == key)
                return entry;
        }

        return null;
    }
}
