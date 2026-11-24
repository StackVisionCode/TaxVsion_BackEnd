using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Timers;

/// <param name="Hours">Cero mientras el tramo sigue corriendo.</param>
public sealed record TaskTimerResponse(
    Guid Id,
    Guid TaskId,
    Guid UserId,
    DateTime StartedAtUtc,
    DateTime? StoppedAtUtc,
    bool IsBillable,
    bool IsRunning,
    decimal Hours,
    bool IsManual = false,
    string? Note = null,
    DateTime? UpdatedAtUtc = null
)
{
    public static TaskTimerResponse From(TaskTimer timer) =>
        new(
            timer.Id,
            timer.TaskId,
            timer.UserId,
            timer.StartedAtUtc,
            timer.StoppedAtUtc,
            timer.IsBillable,
            timer.IsRunning,
            timer.Hours,
            timer.IsManual,
            timer.Note,
            timer.UpdatedAtUtc
        );
}
