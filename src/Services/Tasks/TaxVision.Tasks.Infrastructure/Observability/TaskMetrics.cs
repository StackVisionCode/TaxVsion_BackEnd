using System.Diagnostics.Metrics;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Infrastructure.Observability;

/// <summary>
/// Adaptador OTel de <see cref="ITaskMetrics"/>.
///
/// <para>
/// <b>El Meter se llama <see cref="MeterName"/> y hay que pasarlo como <c>additionalMeterNames</c>.</b>
/// Un Meter con nombre propio que no se registre no exporta nada: los contadores suben en memoria y
/// el dashboard queda vacío sin un solo error.
/// </para>
/// </summary>
public sealed class TaskMetrics : ITaskMetrics, IDisposable
{
    public const string MeterName = "TaxVision.Tasks";

    public string Name => _meter.Name;

    private readonly Meter _meter;
    private readonly Counter<long> _createdTotal;
    private readonly Counter<long> _completedTotal;
    private readonly Counter<long> _blockedTotal;
    private readonly Counter<long> _dependencyCycleRejectedTotal;
    private readonly Counter<long> _reconciliationCorrectionsTotal;
    private readonly Counter<long> _overdueTotal;
    private readonly Histogram<double> _timeToCompleteSeconds;

    /// <param name="meterName">
    /// Sólo los tests lo cambian: un Meter de nombre fijo se escucha desde cualquier ensamblado, y
    /// dos suites corriendo a la vez se cuentan las mediciones entre sí.
    /// </param>
    public TaskMetrics(string? meterName = null)
    {
        _meter = new Meter(meterName ?? MeterName);

        _createdTotal = _meter.CreateCounter<long>("task.created_total", description: "Tasks created");
        _completedTotal = _meter.CreateCounter<long>("task.completed_total", description: "Tasks completed");
        _blockedTotal = _meter.CreateCounter<long>(
            "task.blocked_total",
            description: "Tasks that could not start because a blocker was still open"
        );
        _dependencyCycleRejectedTotal = _meter.CreateCounter<long>(
            "task.dependency_cycle_rejected_total",
            description: "Dependency edges rejected because they would close a cycle"
        );
        _reconciliationCorrectionsTotal = _meter.CreateCounter<long>(
            "task.reconciliation_corrections_total",
            description: "Counter drifts the reconciliation job had to fix"
        );
        _overdueTotal = _meter.CreateCounter<long>(
            "task.overdue_total",
            description: "Tasks found past their due date and still open"
        );
        _timeToCompleteSeconds = _meter.CreateHistogram<double>(
            "task.time_to_complete_seconds",
            unit: "s",
            description: "Wall clock from creation to completion"
        );
    }

    public void RecordCreated(bool hasCustomer) =>
        _createdTotal.Add(1, new KeyValuePair<string, object?>("has_customer", hasCustomer));

    public void RecordCompleted(bool hasCustomer) =>
        _completedTotal.Add(1, new KeyValuePair<string, object?>("has_customer", hasCustomer));

    public void RecordBlocked() => _blockedTotal.Add(1);

    public void RecordDependencyCycleRejected() => _dependencyCycleRejectedTotal.Add(1);

    public void RecordReconciliationCorrections(int count)
    {
        if (count > 0)
            _reconciliationCorrectionsTotal.Add(count);
    }

    public void RecordTimeToCompleteSeconds(double seconds) => _timeToCompleteSeconds.Record(seconds);

    public void RecordOverdue(int count)
    {
        if (count > 0)
            _overdueTotal.Add(count);
    }

    public void Dispose() => _meter.Dispose();
}
