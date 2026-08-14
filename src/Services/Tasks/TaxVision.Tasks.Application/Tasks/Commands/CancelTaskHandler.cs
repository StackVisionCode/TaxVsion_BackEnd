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

public sealed record CancelTaskCommand(Guid TenantId, Guid TaskId, Guid ByUserId, bool HasManageAll, string? Reason);

/// <summary>
/// Cancelar cierra igual que completar, así que dispara las mismas dos cascadas. Una sucesora no
/// puede quedar bloqueada para siempre porque la predecesora se canceló.
/// </summary>
public static class CancelTaskHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        CancelTaskCommand command,
        ITaskRepository tasks,
        ITaskUnblockingService unblocking,
        ITaskHierarchyService hierarchy,
        ITaskSeriesMaterializer seriesMaterializer,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
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
        var cancelled = task.Cancel(command.Reason, command.ByUserId, DateTime.UtcNow);
        if (cancelled.IsFailure)
            return Result.Failure<TaskResponse>(cancelled.Error);

        if (wasOpen)
            await CascadeAsync(command, task, unblocking, hierarchy, seriesMaterializer, bus, correlation, ct);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskResponse.From(task));
    }

    /// <summary>
    /// Cancelar arrastra lo mismo que completar —sucesoras, padre, serie, aviso a Reminder— pero sin
    /// fecha de cierre: la ocurrencia siguiente se calcula desde la regla, no desde un cierre que no
    /// hubo.
    /// </summary>
    private static async Task CascadeAsync(
        CancelTaskCommand command,
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

        var next = await seriesMaterializer.ApplyInstanceClosedAsync(task, null, ct);
        if (next is not null)
            await TaskDueReminder.PublishIfDueAsync(next, bus, correlation);

        await bus.PublishAsync(BuildEvent(task, command, correlation.CorrelationId));
        await bus.PublishAsync(
            TaskReminderContracts.Closed(
                task,
                TaskReminderContracts.ClosureReasons.Cancelled,
                correlation.CorrelationId
            )
        );
    }

    private static TaskCancelledIntegrationEvent BuildEvent(
        TaskItem task,
        CancelTaskCommand command,
        string correlationId
    ) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            TaskId = task.Id,
            Reason = command.Reason!.Trim(),
            CancelledByUserId = command.ByUserId,
        };
}
