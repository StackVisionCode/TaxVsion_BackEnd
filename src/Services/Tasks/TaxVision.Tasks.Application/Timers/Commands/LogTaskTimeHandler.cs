using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Timers.Commands;

public sealed record LogTaskTimeCommand(
    Guid TenantId,
    Guid TaskId,
    Guid UserId,
    DateOnly WorkedOn,
    int Minutes,
    bool IsBillable,
    string? Note
);

/// <summary>
/// Tiempo ya trabajado, anotado a mano. Igual que el temporizador, no exige ser responsable de la tarea: el tiempo
/// queda a nombre de quien lo anota. El día se mide en la zona de la oficina.
/// </summary>
public static class LogTaskTimeHandler
{
    public static async Task<Result<TaskTimerResponse>> Handle(
        LogTaskTimeCommand command,
        ITaskRepository tasks,
        IOfficeTimeZone zones,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdWithTimersAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskTimerResponse>(found.Error);

        var now = DateTime.UtcNow;
        var zone = await zones.ResolveAsync(command.TenantId, ct);
        var day = new WorkDay(command.WorkedOn, LocalDay.StartUtc(command.WorkedOn, zone), CivilDate.Of(now, zone));
        var logged = found.Value.LogTime(command.UserId, day, command.Minutes, command.IsBillable, command.Note, now);
        if (logged.IsFailure)
            return Result.Failure<TaskTimerResponse>(logged.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskTimerResponse.From(logged.Value));
    }
}
