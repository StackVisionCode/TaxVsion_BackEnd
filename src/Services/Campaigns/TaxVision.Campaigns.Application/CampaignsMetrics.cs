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

    public static void RecordScheduleFireSkipped(string reason) =>
        ScheduleFireSkipped.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
