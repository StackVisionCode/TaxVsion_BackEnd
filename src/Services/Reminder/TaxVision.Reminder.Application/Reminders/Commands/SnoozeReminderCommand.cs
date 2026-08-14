using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Reminder.Application.Reminders.Abstractions;

namespace TaxVision.Reminder.Application.Reminders.Commands;

public sealed record SnoozeReminderCommand(Guid TenantId, Guid UserId, Guid ReminderId, int Minutes);

/// <summary>
/// Posponer solo tiene sentido sobre un recordatorio ya disparado; el aggregate rechaza el resto de
/// estados y aplica el tope de <c>MaxSnoozeCount</c>. Reagenda en Quartz después de persistir
/// (ADR-R-04).
/// </summary>
public static class SnoozeReminderHandler
{
    public static async Task<Result<ReminderResponse>> Handle(
        SnoozeReminderCommand command,
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
        var snoozed = reminder.Snooze(TimeSpan.FromMinutes(command.Minutes), DateTime.UtcNow);
        if (snoozed.IsFailure)
            return Result.Failure<ReminderResponse>(snoozed.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await scheduler.RescheduleAsync(reminder.TenantId, reminder.Id, reminder.Schedule.FireAtUtc, ct);
        return Result.Success(ReminderResponse.From(reminder));
    }
}
