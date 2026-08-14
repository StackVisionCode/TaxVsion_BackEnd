using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Abstractions;

/// <summary>
/// Los lookups que pueden fallar devuelven <see cref="Result{T}"/>, no <c>null</c>.
///
/// <para>
/// Todas las lecturas usan <c>IgnoreQueryFilters()</c> con el tenant en el predicado: los handlers
/// corren en el scope de Wolverine, donde el <c>TenantContext</c> está vacío y el filtro global
/// devolvería 0 filas sobre datos que sí existen.
/// </para>
/// </summary>
public interface ITaskRepository
{
    void Add(TaskItem task);

    void Remove(TaskItem task);

    Task<Result<TaskItem>> GetByIdAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);

    /// <summary>La tarea con sus timers cargados. Aparte porque el resto de los caminos no los usa.</summary>
    Task<Result<TaskItem>> GetByIdWithTimersAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);

    Task<Result<TaskItem>> GetByIdWithAttachmentsAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);

    /// <summary>
    /// Sin tenant: el consumer de CloudStorage no corre dentro de un scope HTTP y sólo trae el
    /// <c>fileId</c>. Quien llame compara el tenant del evento contra el dueño real antes de mutar.
    /// </summary>
    Task<TaskItem?> GetByAttachmentFileIdAsync(Guid fileId, CancellationToken ct = default);

    Task<IReadOnlyList<TaskItem>> ListWithAttachmentsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken ct = default
    );

    /// <summary>Hijos directos de una tarea, un nivel por página.</summary>
    Task<PagedResult<TaskItem>> ListSubtasksAsync(
        Guid tenantId,
        Guid parentTaskId,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>Búsqueda por texto del título más los filtros de la bandeja.</summary>
    Task<PagedResult<TaskItem>> SearchAsync(
        Guid tenantId,
        TaskQueryFilter filter,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>
    /// Las abiertas para el tablero. Sin paginar y con tope: un Kanban se pinta entero o no sirve, y
    /// el tope evita que un tenant con miles de tareas tumbe la pantalla.
    /// </summary>
    Task<IReadOnlyList<TaskItem>> ListForBoardAsync(
        Guid tenantId,
        TaskQueryFilter filter,
        int take,
        CancellationToken ct = default
    );

    /// <summary>
    /// Las que vencen dentro del rango. Misma tabla que el tablero: una tarea con vencimiento no es
    /// otra entidad, es la misma vista por fecha.
    /// </summary>
    Task<IReadOnlyList<TaskItem>> ListForCalendarAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? assigneeUserId,
        int take,
        CancellationToken ct = default
    );

    /// <summary>
    /// «Mis tareas». Ordenada por vencimiento ascendente con las que no tienen fecha al final: una
    /// bandeja se lee por urgencia, no por fecha de alta.
    /// </summary>
    Task<PagedResult<TaskItem>> ListForAssigneeAsync(
        Guid tenantId,
        Guid assigneeUserId,
        TaskItemStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>Tareas de un cliente, opcionalmente acotadas a un año fiscal.</summary>
    Task<PagedResult<TaskItem>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        int? taxYear,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>
    /// Ordenada por <c>ClientDueAtUtc</c>: lo que se le pidió al cliente para antes se reclama
    /// primero, y esa fecha no es la de vencimiento de la tarea.
    /// </summary>
    Task<PagedResult<TaskItem>> ListWaitingOnClientAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>Cross-tenant: tareas vencidas y todavía abiertas, para el barrido de toda la instalación.</summary>
    Task<IReadOnlyList<TaskItem>> ListOverdueAsync(DateTime nowUtc, int take, CancellationToken ct = default);

    /// <summary>Cadena de padres hacia arriba, sin incluir la propia tarea.</summary>
    Task<IReadOnlyList<Guid>> GetAncestorIdsAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);

    /// <summary>Hijos directos de varios padres a la vez, para bajar un nivel del árbol por consulta.</summary>
    Task<IReadOnlyList<Guid>> ListChildIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> parentTaskIds,
        CancellationToken ct = default
    );

    /// <summary>Las sucesoras de una cascada, rastreadas para mutarlas en el mismo commit.</summary>
    Task<IReadOnlyList<TaskItem>> ListByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken ct = default
    );
}
