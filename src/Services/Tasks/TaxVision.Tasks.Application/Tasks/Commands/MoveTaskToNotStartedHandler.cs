using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Commands;

public sealed record MoveTaskToNotStartedCommand(Guid TenantId, Guid TaskId, Guid ByUserId, bool HasManageAll);

/// <summary>Devuelve la tarea a Not Started desde el tablero; una tarea cerrada se reabre antes con Reopen.</summary>
public static class MoveTaskToNotStartedHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        MoveTaskToNotStartedCommand command,
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

        var moved = found.Value.MoveToNotStarted(command.ByUserId, DateTime.UtcNow);
        if (moved.IsFailure)
            return Result.Failure<TaskResponse>(moved.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskResponse.From(found.Value));
    }
}
