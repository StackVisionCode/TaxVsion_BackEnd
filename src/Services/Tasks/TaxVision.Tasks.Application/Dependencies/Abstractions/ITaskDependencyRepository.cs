using TaxVision.Tasks.Domain.Dependencies;

namespace TaxVision.Tasks.Application.Dependencies.Abstractions;

/// <summary>Todas las lecturas van con <c>IgnoreQueryFilters()</c> y el tenant en el predicado.</summary>
public interface ITaskDependencyRepository
{
    void Add(TaskDependency dependency);

    void Remove(TaskDependency dependency);

    Task<TaskDependency?> GetAsync(Guid tenantId, Guid taskId, Guid dependsOnTaskId, CancellationToken ct = default);

    /// <summary>
    /// Componente conexo aguas arriba de <paramref name="startTaskId"/>, en una sola consulta
    /// recursiva. Devuelve sucesora → predecesoras, que es lo que consume el domain service.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> LoadUpstreamGraphAsync(
        Guid tenantId,
        Guid startTaskId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Toma <c>UPDLOCK</c> sobre las aristas del tenant. Sin esto, dos requests que crean
    /// <c>A→B</c> y <c>B→A</c> validan contra un grafo sin ciclo y los dos pasan.
    /// </summary>
    Task LockTenantEdgesAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Las predecesoras todavía abiertas de una tarea — el valor real de OpenBlockerCount.</summary>
    Task<int> CountOpenBlockersAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);

    /// <summary>Cross-tenant: las sucesoras directas de una predecesora que acaba de cambiar de estado.</summary>
    Task<IReadOnlyList<Guid>> ListSuccessorIdsAsync(
        Guid tenantId,
        Guid dependsOnTaskId,
        CancellationToken ct = default
    );

    /// <summary>Cross-tenant: tareas cuyos contadores no casan con lo que dicen las filas reales.</summary>
    Task<IReadOnlyList<CounterMismatch>> ListCounterMismatchesAsync(int take, CancellationToken ct = default);
}

/// <param name="StoredBlockers">Lo que dice la fila de la tarea.</param>
/// <param name="ActualBlockers">Lo que dicen las aristas.</param>
public sealed record CounterMismatch(
    Guid TenantId,
    Guid TaskId,
    int StoredBlockers,
    int ActualBlockers,
    int StoredSubtasks,
    int ActualSubtasks
);
