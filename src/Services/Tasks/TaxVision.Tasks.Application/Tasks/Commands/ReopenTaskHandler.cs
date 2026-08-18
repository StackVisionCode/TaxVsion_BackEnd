using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.Commands;

public sealed record ReopenTaskCommand(Guid TenantId, Guid TaskId, Guid ByUserId, bool HasManageAll);

/// <summary>
/// Deshace las dos cascadas del cierre. Sin esto el motor se desincroniza en silencio: las sucesoras
/// quedarían ejecutables aunque su predecesora volvió a estar abierta.
/// </summary>
public static class ReopenTaskHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        ReopenTaskCommand command,
        ITaskRepository tasks,
        ITaskUnblockingService unblocking,
        ITaskHierarchyService hierarchy,
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

        var nowUtc = DateTime.UtcNow;
        var reopened = task.Reopen(command.ByUserId, nowUtc);
        if (reopened.IsFailure)
            return Result.Failure<TaskResponse>(reopened.Error);

        await unblocking.ApplyPredecessorReopenedAsync(command.TenantId, task.Id, ct);
        await hierarchy.ApplyChildReopenedAsync(command.TenantId, task.ParentTaskId, ct);
        await bus.PublishAsync(BuildEvent(task, command.ByUserId, nowUtc, correlation.CorrelationId));

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskResponse.From(task));
    }

    private static TaskReopenedIntegrationEvent BuildEvent(
        TaskItem task,
        Guid byUserId,
        DateTime nowUtc,
        string correlationId
    ) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            TaskId = task.Id,
            ReopenedByUserId = byUserId,
            ReopenedAtUtc = nowUtc,
        };
}
