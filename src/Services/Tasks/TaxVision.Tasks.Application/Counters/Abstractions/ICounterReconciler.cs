namespace TaxVision.Tasks.Application.Counters.Abstractions;

/// <summary>Vive en Application porque <c>ReconcileCounters</c> es <c>internal</c> del dominio.</summary>
public interface ICounterReconciler
{
    /// <summary>Devuelve cuántas tareas corrigió.</summary>
    Task<int> ReconcileAsync(int take, CancellationToken ct = default);
}
