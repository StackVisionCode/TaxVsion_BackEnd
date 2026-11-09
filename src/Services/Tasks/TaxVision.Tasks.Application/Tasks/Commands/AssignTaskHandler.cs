using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Reminders;
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
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskResponse>(found.Error);

        var task = found.Value;
        if (!TaskAccessPolicy.CanMutate(task, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskResponse>(TaskErrors.Forbidden);

        // El aviso y el recordatorio del nuevo asignado los devuelve TaskAssignedDomainEventHandler.
        var assigned = task.Assign(command.AssigneeUserId, command.ByUserId, DateTime.UtcNow);
        if (assigned.IsFailure)
            return Result.Failure<TaskResponse>(assigned.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskResponse.From(task));
    }
}
