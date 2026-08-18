using System.Diagnostics.Metrics;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Infrastructure.Observability;

/// <summary>
/// Adaptador OTel de <see cref="IReminderMetrics"/> — las seis métricas de `00_...` §8.3.
///
/// <para>
/// <b>El Meter se llama <see cref="MeterName"/> y hay que registrarlo a mano.</b>
/// <c>AddTaxVisionOpenTelemetry(config, serviceName)</c> solo hace <c>AddMeter(serviceName)</c>, y
/// acá <c>serviceName</c> es <c>"reminder-service"</c>. Un Meter con nombre propio que no se pase
/// como <c>additionalMeterNames</c> no exporta <b>nada</b>: los contadores suben en memoria y el
/// dashboard queda vacío sin un solo error. Mismo trato que <c>OnboardingMetrics</c> en Auth.
/// </para>
///
/// <para>
/// Los nombres de instrumento van con punto (<c>reminder.fired_total</c>) porque es la convención
/// OTel del repo; el exportador a Prometheus los publica con guion bajo
/// (<c>reminder_fired_total</c>), que es la forma que usan los paneles de Grafana.
/// </para>
/// </summary>
public sealed class ReminderMetrics : IReminderMetrics, IDisposable
{
    public const string MeterName = "TaxVision.Reminder";

    private readonly Meter _meter;
    private readonly Counter<long> _scheduledTotal;
    private readonly Counter<long> _firedTotal;
    private readonly Counter<long> _cancelledTotal;
    private readonly Counter<long> _misfiredTotal;
    private readonly Histogram<double> _fireDelaySeconds;
    private readonly Counter<long> _duplicateSuppressedTotal;

    public ReminderMetrics()
    {
        _meter = new Meter(MeterName);

        _scheduledTotal = _meter.CreateCounter<long>(
            "reminder.scheduled_total",
            description: "Reminders created and armed in the scheduler, by category"
        );
        _firedTotal = _meter.CreateCounter<long>(
            "reminder.fired_total",
            description: "Reminders whose notice actually went out, by category"
        );
        _cancelledTotal = _meter.CreateCounter<long>(
            "reminder.cancelled_total",
            description: "Reminders cancelled, by normalized reason"
        );
        _misfiredTotal = _meter.CreateCounter<long>(
            "reminder.misfired_total",
            description: "Notices discarded for arriving too late (§8.3 — the one that matters), by policy"
        );
        _fireDelaySeconds = _meter.CreateHistogram<double>(
            "reminder.fire_delay_seconds",
            unit: "s",
            description: "Delay between the scheduled FireAtUtc and the actual firing (§8.1 thermometer)"
        );
        _duplicateSuppressedTotal = _meter.CreateCounter<long>(
            "reminder.duplicate_suppressed_total",
            description: "Redeliveries stopped by the RequestKey idempotency of ADR-R-07, by resolution"
        );
    }

    public void RecordScheduled(ReminderCategory category) => _scheduledTotal.Add(1, CategoryTag(category));

    public void RecordFired(ReminderCategory category) => _firedTotal.Add(1, CategoryTag(category));

    public void RecordFireDelaySeconds(double seconds) => _fireDelaySeconds.Record(seconds);

    public void RecordCancelled(string reason) =>
        _cancelledTotal.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void RecordMisfired(string policy) =>
        _misfiredTotal.Add(1, new KeyValuePair<string, object?>("policy", policy));

    public void RecordDuplicateSuppressed(string resolution) =>
        _duplicateSuppressedTotal.Add(1, new KeyValuePair<string, object?>("resolution", resolution));

    public void Dispose() => _meter.Dispose();

    // El enum se etiqueta por nombre, no por su valor numérico: el número ataría el dashboard al
    // orden de los miembros, que es el mismo acoplamiento que los contratos evitan usando string.
    private static KeyValuePair<string, object?> CategoryTag(ReminderCategory category) =>
        new("category", category.ToString());
}
