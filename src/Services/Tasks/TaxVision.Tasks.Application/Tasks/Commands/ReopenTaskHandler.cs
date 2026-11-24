using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Permissions.Abstractions;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.Commands;

public sealed record ReopenTaskCommand(
    Guid TenantId,
    Guid TaskId,
    Guid ByUserId,
    bool HasManageAll,
    bool ReopenParent = false
) : ITaskTransitionCommand;

/// <summary>
/// Deshace las dos cascadas del cierre. Sin esto el motor se desincroniza en silencio: las sucesoras
/// quedarían ejecutables aunque su predecesora volvió a estar abierta. Si el asignado ya no está activo
/// (suspendido o retirado) la tarea vuelve sin asignar, para que la oficina la retome.
/// </summary>
public static class ReopenTaskHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        ReopenTaskCommand command,
        ITaskRepository tasks,
        ITaskSeriesRepository series,
        ITaskUnblockingService unblocking,
        ITaskHierarchyService hierarchy,
        IUserPermissionsProjectionRepository users,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await TaskTransitionService.LoadMutableAsync(tasks, command, ct);
        if (found.IsFailure)
            return Result.Failure<TaskResponse>(found.Error);

        var task = found.Value;
        var reopened = await ReopenWithClosedAncestorsAsync(
            command,
            task,
            tasks,
            series,
            unblocking,
            hierarchy,
            users,
            ct
        );
        if (reopened.IsFailure)
            return Result.Failure<TaskResponse>(reopened.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskResponse.From(task));
    }

    /// <summary>
    /// D-7: una subtarea no se reabre bajo una tarea cerrada. Sin <c>ReopenParent</c> se pregunta antes de
    /// tocar nada; con él se reabren también las ascendientes cerradas.
    /// </summary>
    private static async Task<Result> ReopenWithClosedAncestorsAsync(
        ReopenTaskCommand command,
        TaskItem task,
        ITaskRepository tasks,
        ITaskSeriesRepository series,
        ITaskUnblockingService unblocking,
        ITaskHierarchyService hierarchy,
        IUserPermissionsProjectionRepository users,
        CancellationToken ct
    )
    {
        var closedAncestors = IsClosed(task) ? await ClosedAncestorsAsync(task, tasks, ct) : [];
        if (closedAncestors.Count > 0 && !command.ReopenParent)
            return Result.Failure(TaskErrors.Subtask.ParentClosed);
        if (closedAncestors.Any(a => !TaskAccessPolicy.CanMutate(a, command.ByUserId, command.HasManageAll)))
            return Result.Failure(TaskErrors.Forbidden);
        // Una ocurrencia ya reemplazada (la tarea o su madre) no se reabre: quedarían dos abiertas.
        var allowed = await SeriesOccurrenceReopening.CheckAsync(closedAncestors.Prepend(task), series, ct);
        if (allowed.IsFailure)
            return allowed;

        var nowUtc = DateTime.UtcNow;
        var reopened = task.Reopen(command.ByUserId, nowUtc);
        if (reopened.IsFailure)
            return reopened;

        // TaskReopened y el recordatorio del ciclo nuevo los devuelve TaskReopenedDomainEventHandler.
        foreach (var each in closedAncestors.Prepend(task))
        {
            if (each != task)
                each.Reopen(command.ByUserId, nowUtc);
            await DropInactiveAssigneeAsync(each, users, command.ByUserId, nowUtc, ct);
            await TaskTransitionService.ApplyReopenedAsync(each, unblocking, hierarchy, ct);
            await SeriesOccurrenceReopening.RegisterAsync(each, series, ct);
        }

        return Result.Success();
    }

    private static async Task<IReadOnlyList<TaskItem>> ClosedAncestorsAsync(
        TaskItem task,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        if (task.ParentTaskId is null)
            return [];

        var ancestorIds = await tasks.GetAncestorIdsAsync(task.TenantId, task.Id, ct);
        var ancestors = await tasks.ListByIdsAsync(task.TenantId, ancestorIds, ct);
        return [.. ancestors.Where(IsClosed)];
    }

    private static bool IsClosed(TaskItem task) => TaskItemStatuses.IsClosed(task.Status);

    // Sin proyección no se sabe: se conserva el asignado (mismo comportamiento que antes).
    private static async Task DropInactiveAssigneeAsync(
        TaskItem task,
        IUserPermissionsProjectionRepository users,
        Guid byUserId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (task.AssigneeUserId is not { } assignee)
            return;
        if (await users.GetAsync(task.TenantId, assignee, ct) is { IsActive: false })
            task.Unassign(byUserId, nowUtc);
    }
}
