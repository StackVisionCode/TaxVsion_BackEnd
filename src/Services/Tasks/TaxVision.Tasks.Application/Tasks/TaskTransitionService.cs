using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks;

/// <summary>Lo común de las órdenes que cambian el estado de una tarea.</summary>
public interface ITaskTransitionCommand
{
    Guid TenantId { get; }
    Guid TaskId { get; }
    Guid ByUserId { get; }
    bool HasManageAll { get; }
}

/// <summary>
/// Lo que repiten los handlers de transición: cargar la tarea con permiso y arrastrar el cierre o la
/// reapertura a sucesoras, madre y serie. Estático para que los handlers conserven su firma.
/// </summary>
public static class TaskTransitionService
{
    public static async Task<Result<TaskItem>> LoadMutableAsync(
        ITaskRepository tasks,
        ITaskTransitionCommand command,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return found;

        return TaskAccessPolicy.CanMutate(found.Value, command.ByUserId, command.HasManageAll)
            ? found
            : Result.Failure<TaskItem>(TaskErrors.Forbidden);
    }

    /// <summary>
    /// Solo tras cerrar una tarea que estaba abierta. Devuelve los avisos de desbloqueo. Una cancelada no
    /// pasa fecha de cierre: la serie calcula la siguiente desde la regla.
    /// </summary>
    public static async Task<IReadOnlyList<TaskUnblockedIntegrationEvent>> ApplyClosedAsync(
        TaskItem task,
        ITaskUnblockingService unblocking,
        ITaskHierarchyService hierarchy,
        ITaskSeriesMaterializer seriesMaterializer,
        CancellationToken ct
    )
    {
        var unblocked = await unblocking.ApplyPredecessorClosedAsync(task.TenantId, task.Id, ct);
        await hierarchy.ApplyChildClosedAsync(task.TenantId, task.ParentTaskId, ct);
        var completedAtUtc = task.Status == TaskItemStatus.Completed ? task.CompletedAtUtc : null;
        await seriesMaterializer.ApplyInstanceClosedAsync(task, completedAtUtc, ct);
        return unblocked;
    }

    /// <summary>
    /// "Complete all and finish": cierra las subtareas abiertas de abajo arriba. Valida antes de tocar
    /// nada, porque Wolverine guarda al final aunque la orden falle.
    /// </summary>
    public static async Task<Result<IReadOnlyList<TaskUnblockedIntegrationEvent>>> CompleteOpenSubtasksAsync(
        ITaskRepository tasks,
        TaskItem root,
        Guid byUserId,
        DateTime nowUtc,
        ITaskUnblockingService unblocking,
        ITaskHierarchyService hierarchy,
        ITaskSeriesMaterializer seriesMaterializer,
        CancellationToken ct
    )
    {
        if (root.IsBlocked)
            return Result.Failure<IReadOnlyList<TaskUnblockedIntegrationEvent>>(
                TaskErrors.BlockedByDependencies(root.OpenBlockerCount)
            );

        var levels = await OpenDescendantsByLevelAsync(tasks, root, ct);
        if (levels.SelectMany(level => level).FirstOrDefault(task => task.IsBlocked) is { } blocked)
            return Result.Failure<IReadOnlyList<TaskUnblockedIntegrationEvent>>(
                TaskErrors.Subtask.Blocked(blocked.Title.Value)
            );

        var notices = new List<TaskUnblockedIntegrationEvent>();
        foreach (var task in Enumerable.Reverse(levels).SelectMany(level => level))
        {
            task.Complete(byUserId, nowUtc);
            notices.AddRange(await ApplyClosedAsync(task, unblocking, hierarchy, seriesMaterializer, ct));
        }

        return Result.Success<IReadOnlyList<TaskUnblockedIntegrationEvent>>(notices);
    }

    /// <summary>
    /// D-6: tras cancelar la tarea, sus subtareas abiertas se cancelan con el mismo motivo. Cancelar no
    /// mira bloqueos, así que no hay nada que validar antes.
    /// </summary>
    public static async Task<IReadOnlyList<TaskUnblockedIntegrationEvent>> CancelOpenSubtasksAsync(
        ITaskRepository tasks,
        TaskItem root,
        string reason,
        Guid byUserId,
        DateTime nowUtc,
        ITaskUnblockingService unblocking,
        ITaskHierarchyService hierarchy,
        ITaskSeriesMaterializer seriesMaterializer,
        CancellationToken ct
    )
    {
        var notices = new List<TaskUnblockedIntegrationEvent>();
        foreach (var task in (await OpenDescendantsByLevelAsync(tasks, root, ct)).SelectMany(level => level))
        {
            task.Cancel(reason, byUserId, nowUtc);
            notices.AddRange(await ApplyClosedAsync(task, unblocking, hierarchy, seriesMaterializer, ct));
        }

        return notices;
    }

    private static async Task<List<List<TaskItem>>> OpenDescendantsByLevelAsync(
        ITaskRepository tasks,
        TaskItem root,
        CancellationToken ct
    )
    {
        var levels = new List<List<TaskItem>>();
        var parentIds = new List<Guid> { root.Id };
        while (parentIds.Count > 0)
        {
            var childIds = await tasks.ListChildIdsAsync(root.TenantId, parentIds, ct);
            var children = childIds.Count == 0 ? [] : await tasks.ListByIdsAsync(root.TenantId, childIds, ct);
            var open = children.Where(t => TaskItemStatuses.IsOpen(t.Status));
            levels.Add([.. open]);
            parentIds = [.. children.Select(t => t.Id)];
        }

        return levels;
    }

    public static async Task ApplyReopenedAsync(
        TaskItem task,
        ITaskUnblockingService unblocking,
        ITaskHierarchyService hierarchy,
        CancellationToken ct,
        ITaskSeriesMaterializer? series = null
    )
    {
        await unblocking.ApplyPredecessorReopenedAsync(task.TenantId, task.Id, ct);
        await hierarchy.ApplyChildReopenedAsync(task.TenantId, task.ParentTaskId, ct);
        if (series is not null)
            await series.RegisterReopenedAsync(task, ct);
    }
}
