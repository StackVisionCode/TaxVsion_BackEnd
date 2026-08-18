using System.Diagnostics.Metrics;
using TaxVision.Calendar.Infrastructure.Observability;
using Xunit;

namespace TaxVision.Calendar.Tests.Observability;

/// <summary>
/// Se escucha con <see cref="MeterListener"/> —el mismo mecanismo que usa un exporter OTel— y no con
/// un doble del Meter: es la única forma de comprobar que la medición sale de verdad y con sus tags.
///
/// <para>
/// El Meter va con nombre propio para este test. Con el nombre fijo, un listener suscripto por nombre
/// recibe las mediciones de cualquier instancia del proceso, incluidas las de otra clase corriendo en
/// paralelo.
/// </para>
/// </summary>
public sealed class CalendarMetricsTests : IDisposable
{
    private readonly string _meterName = $"TaxVision.Calendar.Tests.{Guid.NewGuid():N}";
    private readonly List<(string Instrument, double Value, string? Tag)> _measurements = [];
    private readonly MeterListener _listener;

    public CalendarMetricsTests()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == _meterName)
                    listener.EnableMeasurementEvents(instrument);
            },
        };

        _listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, _) => Capture(instrument.Name, measurement, tags)
        );
        _listener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, _) => Capture(instrument.Name, measurement, tags)
        );
        _listener.SetMeasurementEventCallback<int>(
            (instrument, measurement, tags, _) => Capture(instrument.Name, measurement, tags)
        );
        _listener.Start();
    }

    [Fact]
    public void A_recurring_appointment_is_counted_apart_from_a_one_off()
    {
        using var metrics = new CalendarMetrics(_meterName);

        metrics.RecordCreated(isRecurring: true);
        metrics.RecordCreated(isRecurring: false);

        var created = _measurements.FindAll(m => m.Instrument == "appointment.created_total");
        Assert.Equal(2, created.Count);
        Assert.Contains(created, m => m.Tag == "True");
        Assert.Contains(created, m => m.Tag == "False");
    }

    /// <summary>
    /// El termómetro de la consulta de rango: sin esta medición, que un tenant se acerque a las 2.000
    /// series se descubre cuando la agenda tarda, no antes.
    /// </summary>
    [Fact]
    public void The_expansion_reports_both_its_duration_and_how_many_series_it_expanded()
    {
        using var metrics = new CalendarMetrics(_meterName);

        metrics.RecordExpansionDuration(12.5, seriesCount: 40);

        Assert.Contains(_measurements, m => m.Instrument == "occurrence_expansion_duration_ms" && m.Value == 12.5);
        Assert.Contains(_measurements, m => m.Instrument == "series_count_per_tenant" && m.Value == 40);
    }

    [Fact]
    public void A_blocked_conflict_is_told_apart_from_a_warning()
    {
        using var metrics = new CalendarMetrics(_meterName);

        metrics.RecordConflictDetected(blocked: true);
        metrics.RecordConflictDetected(blocked: false);

        var conflicts = _measurements.FindAll(m => m.Instrument == "conflict_detected_total");
        Assert.Equal(2, conflicts.Count);
        Assert.Contains(conflicts, m => m.Tag == "True");
        Assert.Contains(conflicts, m => m.Tag == "False");
    }

    /// <summary>Un feed que responde 404 en masa es un token revocado que alguien sigue poleando.</summary>
    [Fact]
    public void The_feed_counts_the_requests_it_could_not_resolve()
    {
        using var metrics = new CalendarMetrics(_meterName);

        metrics.RecordIcsFeedRequest(found: false);

        var request = Assert.Single(_measurements.FindAll(m => m.Instrument == "ics_feed_requests_total"));
        Assert.Equal("False", request.Tag);
    }

    [Fact]
    public void Moving_and_cancelling_have_their_own_counters()
    {
        using var metrics = new CalendarMetrics(_meterName);

        metrics.RecordRescheduled(isRecurring: true);
        metrics.RecordCancelled(isRecurring: false);

        Assert.Contains(_measurements, m => m.Instrument == "appointment.rescheduled_total");
        Assert.Contains(_measurements, m => m.Instrument == "appointment.cancelled_total");
    }

    private void Capture(string instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string? tag = null;
        foreach (var pair in tags)
        {
            if (pair.Key is "is_recurring" or "blocked" or "found")
                tag = pair.Value?.ToString();
        }

        _measurements.Add((instrument, value, tag));
    }

    public void Dispose() => _listener.Dispose();
}
