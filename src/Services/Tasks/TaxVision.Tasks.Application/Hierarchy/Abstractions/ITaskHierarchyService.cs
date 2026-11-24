namespace TaxVision.Tasks.Application.Hierarchy.Abstractions;

/// <summary>No persiste: muta las tareas rastreadas y las guarda el handler que la llamó.</summary>
public interface ITaskHierarchyService
{
    Task ApplyChildClosedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default);

    Task ApplyChildReopenedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default);

    /// <summary>Manda a la papelera la tarea y todo lo que cuelga de ella; devuelve los ids de la rama.</summary>
    Task<IReadOnlyList<Guid>> DeleteWithDescendantsAsync(
        Guid tenantId,
        Guid taskId,
        Guid byUserId,
        CancellationToken ct = default
    );
}
