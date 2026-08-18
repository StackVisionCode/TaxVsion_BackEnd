using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Commands;

public sealed record StartTaskCommand(Guid TenantId, Guid TaskId, Guid ByUserId, bool HasManageAll);

/// <summary>
/// Empezar una tarea bloqueada devuelve <c>BlockedByDependencies</c>, que es 409 y no 400: el
/// contador baja de forma eventual, así que el reintento puede pasar.
/// </summary>
public static class StartTaskHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        StartTaskCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        ITaskMetrics metrics,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskResponse>(found.Error);

        if (!TaskAccessPolicy.CanMutate(found.Value, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskResponse>(TaskErrors.Forbidden);

        var started = found.Value.Start(command.ByUserId, DateTime.UtcNow);
        if (started.IsFailure && started.Error.Code == TaskErrors.BlockedByDependencies(0).Code)
            metrics.RecordBlocked();

        if (started.IsFailure)
            return Result.Failure<TaskResponse>(started.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskResponse.From(found.Value));
    }
}
