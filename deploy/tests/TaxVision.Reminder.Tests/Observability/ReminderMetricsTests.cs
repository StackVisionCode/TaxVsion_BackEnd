using System.Diagnostics.Metrics;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;
using TaxVision.Reminder.Infrastructure.Observability;

namespace TaxVision.Reminder.Tests.Observability;

/// <summary>
/// El adaptador OTel contra el <c>Meter</c> real. Los tests de handlers usan un doble y prueban
/// <b>qué</b> se mide; esto prueba que lo medido <b>sale</b>: con el nombre de instrumento que espera
/// el panel de Grafana y bajo el nombre de Meter que Program.cs registra como meter adicional. Sin
/// ese registro los contadores suben en memoria y el dashboard queda vacío sin un solo error, así
/// que la parte que se verifica acá es justo la que falla en silencio.
/// </summary>
[Collection(ReminderMetricsCollection.Name)]
public sealed class ReminderMetricsTests
{
    [Fact]
    public void Las_seis_metricas_de_8_3_salen_por_el_Meter_del_servicio()
    {
        var counters = new List<(string Instrument, long Value, string? Tag)>();
        var histograms = new List<(string Instrument, double Value)>();

        using var metrics = new ReminderMetrics();
        using var listener = Listen(counters, histograms);

        metrics.RecordScheduled(ReminderCategory.Calendar);
        metrics.RecordFired(ReminderCategory.Calendar);
        metrics.RecordCancelled(ReminderCancellationReasons.UserRequest);
        metrics.RecordMisfired(ReminderMisfirePolicies.GraceExceeded);
        metrics.RecordDuplicateSuppressed(ReminderDuplicateResolutions.Lookup);
        metrics.RecordFireDelaySeconds(12.5);

        Assert.Equal(
            [
                ("reminder.scheduled_total", 1L, "Calendar"),
                ("reminder.fired_total", 1L, "Calendar"),
                ("reminder.cancelled_total", 1L, ReminderCancellationReasons.UserRequest),
                ("reminder.misfired_total", 1L, ReminderMisfirePolicies.GraceExceeded),
                ("reminder.duplicate_suppressed_total", 1L, ReminderDuplicateResolutions.Lookup),
            ],
            counters
        );
        Assert.Equal([("reminder.fire_delay_seconds", 12.5)], histograms);
    }

    /// <summary>
    /// El tag lleva el <b>nombre</b> del enum, no su número: el número ataría el panel al orden de
    /// los miembros, que es el mismo acoplamiento que los contratos evitan usando string.
    /// </summary>
    [Fact]
    public void La_categoria_se_etiqueta_por_nombre_no_por_su_valor_numerico()
    {
        var counters = new List<(string Instrument, long Value, string? Tag)>();

        using var metrics = new ReminderMetrics();
        using var listener = Listen(counters, histograms: []);

        metrics.RecordFired(ReminderCategory.Note);

        Assert.Equal("Note", Assert.Single(counters).Tag);
    }

    private static MeterListener Listen(
        List<(string Instrument, long Value, string? Tag)> counters,
        List<(string Instrument, double Value)> histograms
    )
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == ReminderMetrics.MeterName)
                    l.EnableMeasurementEvents(instrument);
            },
        };

        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) => counters.Add((instrument.Name, value, FirstTagValue(tags)))
        );
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, _, _) => histograms.Add((instrument.Name, value))
        );
        listener.Start();
        return listener;
    }

    private static string? FirstTagValue(ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        tags.Length == 0 ? null : tags[0].Value?.ToString();
}
