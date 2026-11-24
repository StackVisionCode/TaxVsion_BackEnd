using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>
/// El día en que se trabajó, el inicio de ese día en la zona de la oficina y el hoy de la oficina. Lo calcula quien
/// conoce la zona; el dominio solo compara.
/// </summary>
public sealed record WorkDay(DateOnly Date, DateTime StartsAtUtc, DateOnly Today);

public sealed partial class TaskItem
{
    /// <summary>
    /// Tiempo ya trabajado, anotado después (D-21: también en una tarea completada; en una cancelada no). Siempre a
    /// nombre de quien lo anota.
    /// </summary>
    public Result<TaskTimer> LogTime(
        Guid userId,
        WorkDay day,
        int minutes,
        bool isBillable,
        string? note,
        DateTime nowUtc
    )
    {
        if (Status == TaskItemStatus.Cancelled)
            return Result.Failure<TaskTimer>(TaskErrors.Time.TaskCancelled);
        if (day.Date > day.Today)
            return Result.Failure<TaskTimer>(TaskErrors.Time.DateInFuture);
        var valid = ValidateEntry(minutes, note);
        if (valid.IsFailure)
            return Result.Failure<TaskTimer>(valid.Error);

        var entry = TaskTimer.Logged(Id, userId, day.StartsAtUtc, minutes, isBillable, valid.Value);
        _timers.Add(entry);
        ActualHours += entry.Hours;
        return Result.Success(entry);
    }

    /// <summary>Corrige un tramo cerrado. El total se ajusta con la diferencia.</summary>
    public Result<TaskTimer> EditTimeEntry(
        Guid entryId,
        Guid actorUserId,
        bool canManageAll,
        int minutes,
        bool isBillable,
        string? note,
        DateTime nowUtc
    )
    {
        var entry = EditableEntry(entryId, actorUserId, canManageAll);
        if (entry.IsFailure)
            return entry;
        var valid = ValidateEntry(minutes, note);
        if (valid.IsFailure)
            return Result.Failure<TaskTimer>(valid.Error);

        var previousHours = entry.Value.Hours;
        ActualHours -= previousHours;
        entry.Value.Correct(minutes, isBillable, valid.Value, actorUserId, nowUtc);
        ActualHours += entry.Value.Hours;
        AddDomainEvent(
            new TaskTimeEntryChangedDomainEvent(
                Id,
                TenantId,
                entryId,
                entry.Value.UserId,
                previousHours,
                entry.Value.Hours,
                actorUserId,
                nowUtc
            )
        );
        return entry;
    }

    /// <summary>Borra un tramo cerrado y resta sus horas.</summary>
    public Result RemoveTimeEntry(Guid entryId, Guid actorUserId, bool canManageAll, DateTime nowUtc)
    {
        var entry = EditableEntry(entryId, actorUserId, canManageAll);
        if (entry.IsFailure)
            return entry;

        ActualHours -= entry.Value.Hours;
        _timers.Remove(entry.Value);
        AddDomainEvent(
            new TaskTimeEntryChangedDomainEvent(
                Id,
                TenantId,
                entryId,
                entry.Value.UserId,
                entry.Value.Hours,
                null,
                actorUserId,
                nowUtc
            )
        );
        return Result.Success();
    }

    /// <summary>Con null se quita la estimación.</summary>
    public void ChangeEstimate(EstimatedHours? estimate) => Estimated = estimate;

    private Result<TaskTimer> EditableEntry(Guid entryId, Guid actorUserId, bool canManageAll)
    {
        var entry = _timers.FirstOrDefault(t => t.Id == entryId);
        if (entry is null)
            return Result.Failure<TaskTimer>(TaskErrors.Timer.NotFound);
        if (Status == TaskItemStatus.Cancelled)
            return Result.Failure<TaskTimer>(TaskErrors.Time.TaskCancelled);
        if (entry.UserId != actorUserId && !canManageAll)
            return Result.Failure<TaskTimer>(TaskErrors.Time.NotOwner);
        return entry.IsRunning ? Result.Failure<TaskTimer>(TaskErrors.Time.EntryRunning) : Result.Success(entry);
    }

    /// <summary>Duración entre 1 minuto y 24 horas; la nota, recortada y opcional.</summary>
    private static Result<string?> ValidateEntry(int minutes, string? note)
    {
        if (minutes <= 0 || minutes > TaskTimer.MaxMinutes)
            return Result.Failure<string?>(TaskErrors.Time.DurationInvalid);
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return trimmed is { Length: > TaskTimer.NoteMaxLength }
            ? Result.Failure<string?>(TaskErrors.Time.NoteTooLong)
            : Result.Success(trimmed);
    }
}
