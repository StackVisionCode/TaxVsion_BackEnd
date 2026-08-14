using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Timers.Commands;

public sealed record StartTaskTimerCommand(Guid TenantId, Guid TaskId, Guid UserId, bool IsBillable);

/// <summary>
/// El único camino que abre un timer. No hay chequeo de propiedad de la tarea a propósito: un revisor
/// imputa horas sobre una tarea asignada a otro, y las horas quedan a nombre de quien las trabajó.
/// </summary>
public static class StartTaskTimerHandler
{
    public static async Task<Result<TaskTimerResponse>> Handle(
        StartTaskTimerCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdWithTimersAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskTimerResponse>(found.Error);

        var started = found.Value.StartTimer(command.UserId, command.IsBillable, DateTime.UtcNow);
        if (started.IsFailure)
            return Result.Failure<TaskTimerResponse>(started.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskTimerResponse.From(started.Value));
    }
}
