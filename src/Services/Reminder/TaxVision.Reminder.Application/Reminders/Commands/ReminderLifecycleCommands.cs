using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.Reminders;

namespace TaxVision.Reminder.Application.Reminders.Commands;

// ---------------------------------------------------------------------------
// Los dos finales del recordatorio. Ambos desagendan en Quartz: dejar el trigger vivo haría que el
// job disparara sobre un estado terminal y lo descartara con un log de ruido cada vez.
// ---------------------------------------------------------------------------

public sealed record DismissReminderCommand(Guid TenantId, Guid UserId, Guid ReminderId);

/// <summary>El usuario lo vio y lo cerró.</summary>
public static class DismissReminderHandler
{
    public static async Task<Result<ReminderResponse>> Handle(
        DismissReminderCommand command,
        IReminderRepository reminders,
        IReminderScheduler scheduler,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await reminders.GetOwnedAsync(command.TenantId, command.UserId, command.ReminderId, ct);
        if (found.IsFailure)
            return Result.Failure<ReminderResponse>(found.Error);

        var reminder = found.Value;
        var dismissed = reminder.Dismiss(DateTime.UtcNow);
        if (dismissed.IsFailure)
            return Result.Failure<ReminderResponse>(dismissed.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await scheduler.UnscheduleAsync(reminder.TenantId, reminder.Id, ct);
        return Result.Success(ReminderResponse.From(reminder));
    }
}

public sealed record CancelReminderCommand(Guid TenantId, Guid UserId, Guid ReminderId, string? Reason);

/// <summary>
/// Cancelar exige razón: sin ella, en soporte es imposible distinguir «lo canceló el usuario» de
/// «se cerró el objetivo al que apuntaba».
/// </summary>
public static class CancelReminderHandler
{
    public static async Task<Result<ReminderResponse>> Handle(
        CancelReminderCommand command,
        IReminderRepository reminders,
        IReminderScheduler scheduler,
        IUnitOfWork unitOfWork,
        IReminderMetrics metrics,
        CancellationToken ct
    )
    {
        var found = await reminders.GetOwnedAsync(command.TenantId, command.UserId, command.ReminderId, ct);
        if (found.IsFailure)
            return Result.Failure<ReminderResponse>(found.Error);

        var reminder = found.Value;
        var cancelled = reminder.Cancel(command.Reason ?? string.Empty, DateTime.UtcNow);
        if (cancelled.IsFailure)
            return Result.Failure<ReminderResponse>(cancelled.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await scheduler.UnscheduleAsync(reminder.TenantId, reminder.Id, ct);
        metrics.RecordCancelled(ReminderCancellationReasons.ForMetrics(reminder.CancellationReason));
        return Result.Success(ReminderResponse.From(reminder));
    }
}
