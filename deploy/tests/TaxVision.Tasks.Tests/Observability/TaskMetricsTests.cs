using System.Diagnostics.Metrics;
using TaxVision.Tasks.Infrastructure.Observability;

namespace TaxVision.Tasks.Tests.Observability;

/// <summary>
/// Cada test estrena su propio nombre de Meter. Escuchar el nombre de producción haría que dos
/// suites corriendo a la vez —en el mismo ensamblado o en otro— se contaran las mediciones entre sí,
/// y el fallo sólo aparecería al correr la solución completa.
/// </summary>
public sealed class TaskMetricsTests
{
    [Fact]
    public void Created_carries_the_has_customer_tag()
    {
        var measurements = new List<(long Value, bool HasCustomer)>();
        using var metrics = NewMetrics();
        using var listener = Listen<long>(
            metrics,
            "task.created_total",
            (value, tags) => measurements.Add((value, (bool)tags["has_customer"]!))
        );

        metrics.RecordCreated(hasCustomer: true);
        metrics.RecordCreated(hasCustomer: false);

        Assert.Equal([(1L, true), (1L, false)], measurements);
    }

    /// <summary>
    /// El termómetro del motor: sólo cuenta cuando la reconciliación tuvo que corregir algo. Un cero
    /// no es una medición, es la ausencia de deriva.
    /// </summary>
    [Fact]
    public void Reconciliation_records_nothing_when_there_was_no_drift()
    {
        var measurements = new List<long>();
        using var metrics = NewMetrics();
        using var listener = Listen<long>(
            metrics,
            "task.reconciliation_corrections_total",
            (value, _) => measurements.Add(value)
        );

        metrics.RecordReconciliationCorrections(0);
        metrics.RecordReconciliationCorrections(3);

        Assert.Equal([3L], measurements);
    }

    [Fact]
    public void Time_to_complete_is_recorded_in_seconds()
    {
        var measurements = new List<double>();
        using var metrics = NewMetrics();
        using var listener = Listen<double>(
            metrics,
            "task.time_to_complete_seconds",
            (value, _) => measurements.Add(value)
        );

        metrics.RecordTimeToCompleteSeconds(90.5);

        Assert.Equal([90.5], measurements);
    }

    private static TaskMetrics NewMetrics() => new($"{TaskMetrics.MeterName}.{Guid.NewGuid():N}");

    private static MeterListener Listen<T>(
        TaskMetrics metrics,
        string instrument,
        Action<T, IReadOnlyDictionary<string, object?>> onMeasured
    )
        where T : struct
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (i, l) =>
            {
                if (i.Meter.Name == metrics.Name && i.Name == instrument)
                    l.EnableMeasurementEvents(i);
            },
        };

        listener.SetMeasurementEventCallback<T>(
            (_, value, tags, _) => onMeasured(value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value))
        );
        listener.Start();
        return listener;
    }
}
