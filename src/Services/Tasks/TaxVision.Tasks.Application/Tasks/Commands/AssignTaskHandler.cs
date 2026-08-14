using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.Commands;

public sealed record AssignTaskCommand(
    Guid TenantId,
    Guid TaskId,
    Guid AssigneeUserId,
    Guid ByUserId,
    bool HasManageAll
);

/// <summary>
/// Sin restricción de dirección: un empleado puede asignarle a un admin. El flujo de revisión interna
/// lo exige, en Auth no hay jerarquía que consultar, y el contrapeso es que desasignar siempre está
/// disponible.
/// </summary>
public static class AssignTaskHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        AssignTaskCommand command,
        ITaskRepository tasks,
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

        var previousAssigneeUserId = task.AssigneeUserId;
        var assigned = task.Assign(command.AssigneeUserId, command.ByUserId, DateTime.UtcNow);
        if (assigned.IsFailure)
            return Result.Failure<TaskResponse>(assigned.Error);

        if (previousAssigneeUserId != command.AssigneeUserId)
            await bus.PublishAsync(BuildEvent(task, previousAssigneeUserId, correlation.CorrelationId));

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskResponse.From(task));
    }

    private static TaskAssignedIntegrationEvent BuildEvent(
        TaskItem task,
        Guid? previousAssigneeUserId,
        string correlationId
    ) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            TaskId = task.Id,
            Title = task.Title.Value,
            AssigneeUserId = task.AssigneeUserId!.Value,
            PreviousAssigneeUserId = previousAssigneeUserId,
            DueAtUtc = task.Due?.DueAtUtc,
        };
}
