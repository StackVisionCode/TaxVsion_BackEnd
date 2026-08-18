using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Commands;

public sealed record UnassignTaskCommand(Guid TenantId, Guid TaskId, Guid ByUserId, bool HasManageAll);

/// <summary>Sin evento de integración: nadie fuera de Task reacciona a que una tarea quede libre.</summary>
public static class UnassignTaskHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        UnassignTaskCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskResponse>(found.Error);

        if (!TaskAccessPolicy.CanMutate(found.Value, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskResponse>(TaskErrors.Forbidden);

        var unassigned = found.Value.Unassign(command.ByUserId, DateTime.UtcNow);
        if (unassigned.IsFailure)
            return Result.Failure<TaskResponse>(unassigned.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskResponse.From(found.Value));
    }
}
