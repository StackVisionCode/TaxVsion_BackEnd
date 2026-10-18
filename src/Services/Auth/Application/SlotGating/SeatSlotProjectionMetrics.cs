using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace TaxVision.Auth.Application.SlotGating;

public static class SeatSlotProjectionMetrics
{
    public const string MeterName = "TaxVision.Auth.SeatSlotGating";

    private sealed record Snapshot(int ActiveUsers, int EffectiveCap, int GatedUsers);

    private static readonly ConcurrentDictionary<Guid, Snapshot> Snapshots = new();
    private static readonly Meter Meter = new(MeterName);

    private static readonly ObservableGauge<int> ActiveUsers = Meter.CreateObservableGauge(
        "seats.slot_gate.active_users",
        () => Observe(snapshot => snapshot.ActiveUsers)
    );
    private static readonly ObservableGauge<int> EffectiveCap = Meter.CreateObservableGauge(
        "seats.slot_gate.effective_cap",
        () => Observe(snapshot => snapshot.EffectiveCap)
    );
    private static readonly ObservableGauge<int> GatedUsers = Meter.CreateObservableGauge(
        "seats.slot_gate.gated_users",
        () => Observe(snapshot => snapshot.GatedUsers)
    );

    public static void Record(Guid tenantId, int activeUsers, int effectiveCap, int gatedUsers) =>
        Snapshots[tenantId] = new Snapshot(activeUsers, effectiveCap, gatedUsers);

    private static IEnumerable<Measurement<int>> Observe(Func<Snapshot, int> valueSelector) =>
        Snapshots.Select(pair => new Measurement<int>(
            valueSelector(pair.Value),
            new KeyValuePair<string, object?>("tenant_id", pair.Key)
        ));
}
