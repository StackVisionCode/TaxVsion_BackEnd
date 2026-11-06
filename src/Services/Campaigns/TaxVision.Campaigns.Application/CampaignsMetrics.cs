using System.Diagnostics.Metrics;

namespace TaxVision.Campaigns.Application;

/// <summary>Métricas de Campaigns. El Meter se llama como el servicio, que OpenTelemetry ya exporta.</summary>
public static class CampaignsMetrics
{
    private static readonly Meter Meter = new("campaigns-service");

    private static readonly Counter<long> ScheduleFireSkipped = Meter.CreateCounter<long>(
        "campaigns_schedule_fire_skipped_total",
        description: "Schedule slots skipped without a run"
    );

    private static readonly Counter<long> DispatchResultUnmatched = Meter.CreateCounter<long>(
        "campaigns_dispatch_result_unmatched_total",
        description: "Dispatch results for a unit that does not exist in its run"
    );

    public static void RecordScheduleFireSkipped(string reason) =>
        ScheduleFireSkipped.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public static void RecordDispatchResultUnmatched() => DispatchResultUnmatched.Add(1);
}
