using BuildingBlocks.Common;
using BuildingBlocks.Contracts.Work;

namespace TaxVision.Tasks.Application.Work;

/// <summary>
/// Lectura del trabajo abierto en SQL: tareas asignadas y pedidos al cliente de los que la persona es responsable.
/// <c>visibleTo</c> aplica el ABAC del listado para quien mira (null = sin restricción), también a los conteos.
/// </summary>
public interface ITaskWorkReader
{
    /// <summary>Un conteo por tipo: <see cref="WorkKinds.Tasks"/> y <see cref="WorkKinds.ClientRequests"/>.</summary>
    Task<IReadOnlyList<WorkCount>> CountAsync(
        Guid tenantId,
        Guid userId,
        Guid? visibleTo,
        WorkWindow window,
        CancellationToken ct
    );

    /// <summary>
    /// Lo que espera al cliente en toda la oficina, sin filtro de responsable: tareas en <c>WaitingOnClient</c> y
    /// pedidos pendientes, con el ABAC de quien mira.
    /// </summary>
    Task<IReadOnlyList<OfficeWaitingCount>> CountOfficeWaitingAsync(
        Guid tenantId,
        Guid? visibleTo,
        CancellationToken ct
    );

    /// <summary>Hasta <paramref name="take"/> por tipo, el vencimiento más viejo primero.</summary>
    Task<IReadOnlyList<WorkItem>> TopAsync(Guid tenantId, Guid userId, Guid? visibleTo, int take, CancellationToken ct);

    /// <summary>Abiertos y vencidos por persona (ambos tipos sumados); solo filas con algo.</summary>
    Task<IReadOnlyList<WorkBatchRow>> CountByUserAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> userIds,
        Guid? visibleTo,
        WorkWindow window,
        CancellationToken ct
    );

    /// <summary>Una página de un tipo (tasks o client-requests): el mismo universo que el conteo y el orden del top.</summary>
    Task<PagedResult<WorkItem>> ItemsAsync(
        Guid tenantId,
        Guid userId,
        Guid? visibleTo,
        string kind,
        int page,
        int size,
        CancellationToken ct
    );

    /// <summary>Títulos sin recortar de lo que existe en el tenant y quien mira puede leer, en estado cualquiera.</summary>
    Task<IReadOnlyList<WorkTitle>> TitlesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> taskIds,
        IReadOnlyCollection<Guid> clientRequestIds,
        Guid? visibleTo,
        CancellationToken ct
    );
}
