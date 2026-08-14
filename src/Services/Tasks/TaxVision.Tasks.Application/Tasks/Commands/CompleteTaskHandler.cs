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

public sealed record CompleteTaskCommand(Guid TenantId, Guid TaskId, Guid ByUserId, bool HasManageAll);

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
        ICorrelationContext correlation,
        ITaskMetrics metrics,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskResponse>(found.Error);

        var task = found.Value;
        if (!TaskAccessPolicy.CanMutate(task, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskResponse>(TaskErrors.Forbidden);

        var wasOpen = task.Status is not (TaskItemStatus.Completed or TaskItemStatus.Cancelled);
        var completed = task.Complete(command.ByUserId, DateTime.UtcNow);
        if (completed.IsFailure)
            return Result.Failure<TaskResponse>(completed.Error);

        if (wasOpen)
            await CascadeAsync(command, task, unblocking, hierarchy, seriesMaterializer, bus, correlation, ct);

        await unitOfWork.SaveChangesAsync(ct);
        metrics.RecordCompleted(task.Reference.CustomerId is not null);
        metrics.RecordTimeToCompleteSeconds((DateTime.UtcNow - task.CreatedAtUtc).TotalSeconds);
        return Result.Success(TaskResponse.From(task));
    }

    /// <summary>
    /// Lo que arrastra cerrar una tarea: desbloquea sucesoras, baja el contador del padre, materializa
    /// la siguiente ocurrencia de la serie y avisa a Reminder. Sólo la primera vez.
    /// </summary>
    private static async Task CascadeAsync(
        CompleteTaskCommand command,
        TaskItem task,
        ITaskUnblockingService unblocking,
        ITaskHierarchyService hierarchy,
        ITaskSeriesMaterializer seriesMaterializer,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        await unblocking.ApplyPredecessorClosedAsync(command.TenantId, task.Id, ct);
        await hierarchy.ApplyChildClosedAsync(command.TenantId, task.ParentTaskId, ct);

        var next = await seriesMaterializer.ApplyInstanceClosedAsync(task, task.CompletedAtUtc, ct);
        if (next is not null)
            await TaskDueReminder.PublishIfDueAsync(next, bus, correlation);

        await bus.PublishAsync(BuildEvent(task, command.ByUserId, correlation.CorrelationId));
        await bus.PublishAsync(
            TaskReminderContracts.Closed(
                task,
                TaskReminderContracts.ClosureReasons.Completed,
                correlation.CorrelationId
            )
        );
    }

    private static TaskCompletedIntegrationEvent BuildEvent(TaskItem task, Guid byUserId, string correlationId) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            TaskId = task.Id,
            Title = task.Title.Value,
            CompletedByUserId = byUserId,
            CompletedAtUtc = task.CompletedAtUtc ?? DateTime.UtcNow,
            CustomerId = task.Reference.CustomerId,
            TaxYear = task.Reference.TaxYear,
        };
}
