using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Application.Reminders.Commands;

public sealed record UpdateReminderScheduleCommand(
    Guid TenantId,
    Guid UserId,
    Guid ReminderId,
    DateTime? FireAtUtc,
    DateTime? AnchorAtUtc,
    int? LeadMinutes
);

/// <summary>
/// Mueve la hora de disparo y reagenda en Quartz después de persistir (ADR-R-04). Resuelve el
/// aggregate por (tenant, usuario, id): un recordatorio ajeno se ve como inexistente, nunca como
/// prohibido.
/// </summary>
public static class UpdateReminderScheduleHandler
{
    public static async Task<Result<ReminderResponse>> Handle(
        UpdateReminderScheduleCommand command,
        IReminderRepository reminders,
        IReminderScheduler scheduler,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await reminders.GetOwnedAsync(command.TenantId, command.UserId, command.ReminderId, ct);
        if (found.IsFailure)
            return Result.Failure<ReminderResponse>(found.Error);

        var nowUtc = DateTime.UtcNow;
        var schedule = command is { AnchorAtUtc: { } anchor, LeadMinutes: { } lead }
            ? ReminderSchedule.Anchored(anchor, lead, nowUtc)
            : ReminderSchedule.Absolute(command.FireAtUtc ?? default, nowUtc);
        if (schedule.IsFailure)
            return Result.Failure<ReminderResponse>(schedule.Error);

        var reminder = found.Value;
        var changed = reminder.ChangeSchedule(schedule.Value, nowUtc);
        if (changed.IsFailure)
            return Result.Failure<ReminderResponse>(changed.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await scheduler.RescheduleAsync(reminder.TenantId, reminder.Id, reminder.Schedule.FireAtUtc, ct);
        return Result.Success(ReminderResponse.From(reminder));
    }
}
