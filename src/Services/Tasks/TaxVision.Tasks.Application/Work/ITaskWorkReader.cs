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
}
