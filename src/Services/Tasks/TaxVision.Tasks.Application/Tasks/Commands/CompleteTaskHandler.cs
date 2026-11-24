using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.Commands;

public sealed record CompleteTaskCommand(
    Guid TenantId,
    Guid TaskId,
    Guid ByUserId,
    bool HasManageAll,
    bool CompleteOpenSubtasks = false
) : ITaskTransitionCommand;

/// <summary>
/// Cerrar una tarea mueve dos contadores ajenos: el de bloqueadores de sus sucesoras y el de
/// subtareas abiertas de su padre. Los dos se tocan sólo si la tarea estaba abierta — completar dos
/// veces es <c>Success</c> en el dominio y sin esa guarda el segundo intento descontaría de nuevo.
/// </summary>
public static class CompleteTaskHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        CompleteTaskCommand command,
        ITaskRepository tasks,
        ITaskUnblockingService unblocking,
        ITaskHierarchyService hierarchy,
        ITaskSeriesMaterializer seriesMaterializer,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ITaskMetrics metrics,
        CancellationToken ct
    )
    {
        var found = await TaskTransitionService.LoadMutableAsync(tasks, command, ct);
        if (found.IsFailure)
            return Result.Failure<TaskResponse>(found.Error);

        var task = found.Value;
        var closed = await CloseAsync(command, task, tasks, unblocking, hierarchy, seriesMaterializer, ct);
        if (closed.IsFailure)
            return Result.Failure<TaskResponse>(closed.Error);

        await unitOfWork.SaveChangesAsync(ct);

        foreach (var notice in closed.Value)
            await bus.PublishAsync(notice);

        metrics.RecordCompleted(task.Reference.CustomerId is not null);
        metrics.RecordTimeToCompleteSeconds((DateTime.UtcNow - task.CreatedAtUtc).TotalSeconds);
        return Result.Success(TaskResponse.From(task));
    }

    /// <summary>
    /// Las subtareas primero (si se pidió) y después la tarea. Las cascadas solo corren si estaba abierta:
    /// completar dos veces es <c>Success</c> y no debe descontar otra vez.
    /// </summary>
    private static async Task<Result<IReadOnlyList<TaskUnblockedIntegrationEvent>>> CloseAsync(
        CompleteTaskCommand command,
        TaskItem task,
        ITaskRepository tasks,
        ITaskUnblockingService unblocking,
        ITaskHierarchyService hierarchy,
        ITaskSeriesMaterializer seriesMaterializer,
        CancellationToken ct
    )
    {
        var wasOpen = TaskItemStatuses.IsOpen(task.Status);
        var nowUtc = DateTime.UtcNow;
        var notices = new List<TaskUnblockedIntegrationEvent>();
        if (wasOpen && command.CompleteOpenSubtasks)
        {
            var subtasks = await TaskTransitionService.CompleteOpenSubtasksAsync(
                tasks,
                task,
                command.ByUserId,
                nowUtc,
                unblocking,
                hierarchy,
                seriesMaterializer,
                ct
            );
            if (subtasks.IsFailure)
                return Result.Failure<IReadOnlyList<TaskUnblockedIntegrationEvent>>(subtasks.Error);
            notices.AddRange(subtasks.Value);
        }

        var completed = task.Complete(command.ByUserId, nowUtc);
        if (completed.IsFailure)
            return Result.Failure<IReadOnlyList<TaskUnblockedIntegrationEvent>>(completed.Error);

        if (wasOpen)
            notices.AddRange(
                await TaskTransitionService.ApplyClosedAsync(task, unblocking, hierarchy, seriesMaterializer, ct)
            );
        return Result.Success<IReadOnlyList<TaskUnblockedIntegrationEvent>>(notices);
    }
}
