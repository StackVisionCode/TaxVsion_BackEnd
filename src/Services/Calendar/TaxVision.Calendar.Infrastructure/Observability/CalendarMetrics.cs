using System.Diagnostics.Metrics;
using TaxVision.Calendar.Application.Observability;

namespace TaxVision.Calendar.Infrastructure.Observability;

/// <summary>
/// Adaptador OTel de <see cref="ICalendarMetrics"/>.
///
/// <para>
/// <b>El Meter se llama <see cref="MeterName"/> y hay que pasarlo como <c>additionalMeterNames</c>.</b>
/// Un Meter con nombre propio que no se registre no exporta nada: los contadores suben en memoria y el
/// dashboard queda vacío sin un solo error.
/// </para>
/// </summary>
public sealed class CalendarMetrics : ICalendarMetrics, IDisposable
{
    public const string MeterName = "TaxVision.Calendar";

    public string Name => _meter.Name;

    private readonly Meter _meter;
    private readonly Counter<long> _createdTotal;
    private readonly Counter<long> _rescheduledTotal;
    private readonly Counter<long> _cancelledTotal;
    private readonly Counter<long> _conflictDetectedTotal;
    private readonly Counter<long> _icsFeedRequestsTotal;
    private readonly Counter<long> _icsFeedStaleTotal;
    private readonly Histogram<double> _expansionDuration;
    private readonly Histogram<int> _seriesPerQuery;

    /// <param name="meterName">
    /// Sólo los tests lo cambian: un Meter de nombre fijo se escucha desde cualquier ensamblado, y dos
    /// suites corriendo a la vez se cuentan las mediciones entre sí.
    /// </param>
    public CalendarMetrics(string? meterName = null)
    {
        _meter = new Meter(meterName ?? MeterName);

        _createdTotal = _meter.CreateCounter<long>("appointment.created_total", description: "Appointments created");
        _rescheduledTotal = _meter.CreateCounter<long>(
            "appointment.rescheduled_total",
            description: "Appointments moved"
        );
        _cancelledTotal = _meter.CreateCounter<long>(
            "appointment.cancelled_total",
            description: "Appointments cancelled"
        );
        _conflictDetectedTotal = _meter.CreateCounter<long>(
            "conflict_detected_total",
            description: "Overlaps found while scheduling"
        );
        _icsFeedRequestsTotal = _meter.CreateCounter<long>(
            "ics_feed_requests_total",
            description: "Requests to the public .ics feed"
        );
        // Sin `unit`: el exporter de Prometheus se la agrega al nombre, y con el sufijo ya en el
        // nombre sale `occurrence_expansion_duration_ms_milliseconds`. Medido contra el colector.
        _icsFeedStaleTotal = _meter.CreateCounter<long>(
            "ics_feed_stale_total",
            description: "Feed responses served from the last good copy after a live read failed"
        );
        _expansionDuration = _meter.CreateHistogram<double>(
            "occurrence_expansion_duration_ms",
            description: "Wall clock spent expanding series for a range query, in milliseconds"
        );
        _seriesPerQuery = _meter.CreateHistogram<int>(
            "series_count_per_tenant",
            description: "Active series a range query had to expand"
        );
    }

    public void RecordCreated(bool isRecurring) =>
        _createdTotal.Add(1, new KeyValuePair<string, object?>("is_recurring", isRecurring));

    public void RecordRescheduled(bool isRecurring) =>
        _rescheduledTotal.Add(1, new KeyValuePair<string, object?>("is_recurring", isRecurring));

    public void RecordCancelled(bool isRecurring) =>
        _cancelledTotal.Add(1, new KeyValuePair<string, object?>("is_recurring", isRecurring));

    public void RecordExpansionDuration(double milliseconds, int seriesCount)
    {
        _expansionDuration.Record(milliseconds);
        _seriesPerQuery.Record(seriesCount);
    }

    public void RecordConflictDetected(bool blocked) =>
        _conflictDetectedTotal.Add(1, new KeyValuePair<string, object?>("blocked", blocked));

    public void RecordIcsFeedRequest(bool found) =>
        _icsFeedRequestsTotal.Add(1, new KeyValuePair<string, object?>("found", found));

    public void RecordIcsFeedStale() => _icsFeedStaleTotal.Add(1);

    public void Dispose() => _meter.Dispose();
}
