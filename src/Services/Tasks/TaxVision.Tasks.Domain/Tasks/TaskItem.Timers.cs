using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>Temporizadores: uno en marcha por persona y tarea.</summary>
public sealed partial class TaskItem
{
    /// <summary>
    /// El único camino que abre un timer. Ni <see cref="Create"/>, ni <see cref="Assign"/>, ni
    /// <see cref="Complete"/>, ni ningún consumer lo llaman: las horas facturables las decide una
    /// persona, no el sistema.
    /// </summary>
    public Result<TaskTimer> StartTimer(Guid userId, bool isBillable, DateTime nowUtc)
    {
        if (IsClosed)
            return Result.Failure<TaskTimer>(TaskErrors.InvalidTransition(Status, nameof(StartTimer)));

        foreach (var running in _timers)
        {
            if (running.IsRunning && running.UserId == userId)
                return Result.Failure<TaskTimer>(TaskErrors.Timer.AlreadyRunning);
        }

        var timer = TaskTimer.Start(Id, userId, isBillable, nowUtc);
        _timers.Add(timer);
        return Result.Success(timer);
    }

    /// <summary>
    /// Sólo lo para quien lo abrió. Se puede parar sobre una tarea ya cerrada: si no, el tramo
    /// quedaría corriendo para siempre y las horas imputadas nunca cuadrarían.
    /// </summary>
    public Result<TaskTimer> StopTimer(Guid timerId, Guid userId, DateTime nowUtc)
    {
        foreach (var timer in _timers)
        {
            if (timer.Id != timerId)
                continue;

            if (timer.UserId != userId)
                return Result.Failure<TaskTimer>(TaskErrors.Timer.NotOwner);

            if (!timer.IsRunning)
                return Result.Failure<TaskTimer>(TaskErrors.Timer.NotRunning);

            timer.Stop(nowUtc);
            ActualHours += timer.Hours;
            return Result.Success(timer);
        }

        return Result.Failure<TaskTimer>(TaskErrors.Timer.NotFound);
    }

    /// <summary>
    /// Cierre por sistema de los timers en marcha de alguien que ya no puede pararlos (retirado). Imputa
    /// cada tramo igual que <see cref="StopTimer"/>; devuelve cuántos cerró (0 si no había, idempotente).
    /// </summary>
    internal int StopRunningTimersOf(Guid userId, DateTime nowUtc)
    {
        var stopped = 0;
        foreach (var timer in _timers)
        {
            if (!timer.IsRunning || timer.UserId != userId)
                continue;

            timer.Stop(nowUtc);
            ActualHours += timer.Hours;
            stopped++;
        }

        return stopped;
    }
}
