using System.Diagnostics.Metrics;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Observability;

/// <summary>Meter singleton del webhook del proveedor; se recolecta vía <c>AddTaxVisionOpenTelemetry</c>.</summary>
public sealed class ProviderWebhookMetrics : IProviderWebhookMetrics, IDisposable
{
    public const string MeterName = "TaxVision.Notification.ProviderWebhooks";

    private readonly Meter _meter;
    private readonly Counter<long> _unroutedTotal;

    public ProviderWebhookMetrics()
    {
        _meter = new Meter(MeterName);
        _unroutedTotal = _meter.CreateCounter<long>("smtp2go_webhook_unrouted_total");
    }

    public void RecordUnrouted(string source) =>
        _unroutedTotal.Add(1, new KeyValuePair<string, object?>("source", source));

    public void Dispose() => _meter.Dispose();
}
