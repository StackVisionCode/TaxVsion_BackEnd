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

public sealed record CancelTaskCommand(Guid TenantId, Guid TaskId, Guid ByUserId, bool HasManageAll, string? Reason)
    : ITaskTransitionCommand;

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

        return Result.Success(TaskResponse.From(task));
    }

    /// <summary>
    /// La tarea primero: si falta el motivo no se toca nada. Las cascadas solo si estaba abierta.
    /// </summary>
    private static async Task<Result<IReadOnlyList<TaskUnblockedIntegrationEvent>>> CloseAsync(
        CancelTaskCommand command,
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
        var cancelled = task.Cancel(command.Reason, command.ByUserId, nowUtc);
        if (cancelled.IsFailure)
            return Result.Failure<IReadOnlyList<TaskUnblockedIntegrationEvent>>(cancelled.Error);
        if (!wasOpen)
            return Result.Success<IReadOnlyList<TaskUnblockedIntegrationEvent>>([]);

        var notices = new List<TaskUnblockedIntegrationEvent>(
            await TaskTransitionService.CancelOpenSubtasksAsync(
                tasks,
                task,
                command.Reason!,
                command.ByUserId,
                nowUtc,
                unblocking,
                hierarchy,
                seriesMaterializer,
                ct
            )
        );
        notices.AddRange(
            await TaskTransitionService.ApplyClosedAsync(task, unblocking, hierarchy, seriesMaterializer, ct)
        );
        return Result.Success<IReadOnlyList<TaskUnblockedIntegrationEvent>>(notices);
    }
}
