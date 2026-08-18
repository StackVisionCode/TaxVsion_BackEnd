namespace TaxVision.Tasks.Application.Hierarchy.Abstractions;

/// <summary>No persiste: muta las tareas rastreadas y las guarda el handler que la llamó.</summary>
public interface ITaskHierarchyService
{
    Task ApplyChildClosedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default);

    Task ApplyChildReopenedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default);

    /// <summary>Borra la tarea y todo lo que cuelga de ella, nivel por nivel.</summary>
    Task DeleteWithDescendantsAsync(Guid tenantId, Guid taskId, Guid byUserId, CancellationToken ct = default);
}
