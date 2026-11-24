using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Statuses.Abstractions;

/// <summary>
/// Lecturas con <c>IgnoreQueryFilters()</c> y el tenant en el predicado, igual que el resto: los
/// handlers corren en el scope de Wolverine, sin <c>TenantContext</c>.
/// </summary>
public interface IWorkflowStatusRepository
{
    void Add(WorkflowStatus status);

    void Remove(WorkflowStatus status);

    Task<Result<WorkflowStatus>> GetByIdAsync(Guid tenantId, Guid statusId, CancellationToken ct = default);

    /// <summary>Por código: es el identificador público. Un estado de otra oficina no aparece.</summary>
    Task<WorkflowStatus?> GetByCodeAsync(Guid tenantId, TaskLabelCode code, CancellationToken ct = default);

    Task<bool> CodeExistsAsync(
        Guid tenantId,
        TaskLabelCode code,
        Guid? excludingStatusId,
        CancellationToken ct = default
    );

    /// <summary>Ordenado por <c>SortOrder</c>: es el orden en que la oficina quiere verlos.</summary>
    Task<IReadOnlyList<WorkflowStatus>> ListAsync(
        Guid tenantId,
        bool includeArchived = false,
        CancellationToken ct = default
    );

    /// <summary>Todos los códigos usados alguna vez, archivados incluidos: un código no se reutiliza.</summary>
    Task<IReadOnlySet<string>> ListCodesAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Entre los activos, sin distinguir mayúsculas.</summary>
    Task<bool> NameTakenAsync(
        Guid tenantId,
        string displayName,
        Guid? excludingStatusId,
        CancellationToken ct = default
    );

    Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default);

    Task<int> CountOpenTasksAsync(Guid tenantId, Guid statusId, CancellationToken ct = default);

    /// <summary>Tareas abiertas por etapa y código guardado (null = el estado por defecto de la etapa).</summary>
    Task<IReadOnlyList<OpenTaskTally>> CountOpenTasksByCodeAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>¿Lo tuvo alguna vez una tarea? Si sí, ya no cambia de etapa ni se borra.</summary>
    Task<bool> IsUsedAsync(Guid tenantId, Guid statusId, CancellationToken ct = default);

    /// <summary>
    /// Mueve en un solo <c>UPDATE</c> las tareas abiertas de un estado a otro de su misma etapa. Las cerradas
    /// lo conservan. Idempotente.
    /// </summary>
    Task<int> MoveOpenTasksAsync(
        Guid tenantId,
        Guid fromStatusId,
        WorkflowStatusRef target,
        CancellationToken ct = default
    );

    /// <summary>Exige que la fila siga en esta versión al guardar; si otro la cambió, el guardado da 409.</summary>
    void ExpectVersion(WorkflowStatus status, byte[] rowVersion);

    /// <summary>Tareas abiertas que quedaron en un estado archivado (se cruzaron con el archivado).</summary>
    Task<int> ReleaseOpenTasksFromArchivedAsync(int take, CancellationToken ct = default);
}

public sealed record OpenTaskTally(TaskItemStatus Category, string? Code, int Count);
