using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Timers.Commands;

public sealed record StopTaskTimerCommand(Guid TenantId, Guid TaskId, Guid TimerId, Guid UserId);

public static class StopTaskTimerHandler
{
    public static async Task<Result<TaskTimerResponse>> Handle(
        StopTaskTimerCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdWithTimersAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskTimerResponse>(found.Error);

        var stopped = found.Value.StopTimer(command.TimerId, command.UserId, DateTime.UtcNow);
        if (stopped.IsFailure)
            return Result.Failure<TaskTimerResponse>(stopped.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskTimerResponse.From(stopped.Value));
    }
}
