using System.Diagnostics.Metrics;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Observability;

/// <summary>
/// Impl concreta del `Meter` de dispatcher (Fase 11). Un solo counter con
/// labels `kind` + `channel` + `outcome` — Prometheus puede slicing por
/// cualquier combinación sin forzarnos a mantener 4 counters paralelos.
/// Recolectado por `AddTaxVisionOpenTelemetry(..., MeterName)` en Program.cs.
/// </summary>
public sealed class NotificationDispatchMetrics : INotificationDispatchMetrics, IDisposable
{
    public const string MeterName = "TaxVision.Notification.Dispatch";

    private readonly Meter _meter;
    private readonly Counter<long> _dispatchedTotal;

    public NotificationDispatchMetrics()
    {
        _meter = new Meter(MeterName);
        _dispatchedTotal = _meter.CreateCounter<long>(
            "notification.dispatched_total",
            unit: "{notification}",
            description: "Notificaciones por kind × channel × outcome del pipeline del dispatcher."
        );
    }

    public void RecordSent(string kind, string channel) => Record(kind, channel, "sent");

    public void RecordSuppressedByPreference(string kind, string channel) =>
        Record(kind, channel, "suppressed_preference");

    public void RecordFailed(string kind, string channel) => Record(kind, channel, "failed");

    public void RecordNoRecipient(string kind, string channel) => Record(kind, channel, "no_recipient");

    private void Record(string kind, string channel, string outcome) =>
        _dispatchedTotal.Add(
            1,
            new KeyValuePair<string, object?>("kind", kind),
            new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("outcome", outcome)
        );

    public void Dispose() => _meter.Dispose();
}
