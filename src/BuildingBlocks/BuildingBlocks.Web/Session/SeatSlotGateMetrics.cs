using System.Diagnostics.Metrics;

namespace BuildingBlocks.Web.Session;

public static class SeatSlotGateMetrics
{
    public const string MeterName = "BuildingBlocks.SeatSlotGating";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Decisions = Meter.CreateCounter<long>("seats.slot_gate.decisions_total");
    public static readonly Counter<long> Failures = Meter.CreateCounter<long>("seats.slot_gate.failures_total");
}
