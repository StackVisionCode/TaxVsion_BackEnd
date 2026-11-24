using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Commands;

public sealed record TakeTaskCommand(Guid TenantId, Guid TaskId, Guid ByUserId, bool CanViewAll);

/// <summary>
/// Tomar para uno mismo una tarea sin asignar que se puede ver (la bandeja común): basta con <c>tasks.write</c>.
/// Quitársela a otro sigue siendo reasignar y pide <c>tasks.assign</c>.
/// </summary>
public static class TakeTaskHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        TakeTaskCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var viewer = TaskReadGate.ViewerOf(visibility, command.ByUserId, command.CanViewAll);
        var visible = await TaskReadGate.EnsureVisibleAsync(tasks, command.TenantId, command.TaskId, viewer, ct);
        if (visible.IsFailure)
            return Result.Failure<TaskResponse>(visible.Error);

        var found = await tasks.GetByIdAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskResponse>(found.Error);

        var task = found.Value;
        if (task.AssigneeUserId is { } holder && holder != command.ByUserId)
            return Result.Failure<TaskResponse>(TaskErrors.AlreadyAssigned);

        // El aviso y el recordatorio del nuevo asignado los devuelve TaskAssignedDomainEventHandler.
        var assigned = task.Assign(command.ByUserId, command.ByUserId, DateTime.UtcNow);
        if (assigned.IsFailure)
            return Result.Failure<TaskResponse>(assigned.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskResponse.From(task));
    }
}
