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
            await CascadeAsync(command, task, unblocking, hierarchy, seriesMaterializer, ct);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskResponse.From(task));
    }

    /// <summary>
    /// Cancelar arrastra lo mismo que completar —sucesoras, padre y serie— pero sin fecha de cierre: la
    /// ocurrencia siguiente se calcula desde la regla. Los avisos los devuelve TaskCancelledDomainEventHandler.
    /// </summary>
    private static async Task CascadeAsync(
        CancelTaskCommand command,
        TaskItem task,
        ITaskUnblockingService unblocking,
        ITaskHierarchyService hierarchy,
        ITaskSeriesMaterializer seriesMaterializer,
        CancellationToken ct
    )
    {
        await unblocking.ApplyPredecessorClosedAsync(command.TenantId, task.Id, ct);
        await hierarchy.ApplyChildClosedAsync(command.TenantId, task.ParentTaskId, ct);

        await seriesMaterializer.ApplyInstanceClosedAsync(task, null, ct);
    }
}
