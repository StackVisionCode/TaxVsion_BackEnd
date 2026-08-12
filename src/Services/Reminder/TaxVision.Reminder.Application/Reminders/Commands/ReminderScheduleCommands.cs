using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Application.Reminders.Commands;

// ---------------------------------------------------------------------------
// Los tres comandos que mueven la hora de disparo. Todos reagendan en Quartz después de persistir
// (ADR-R-04) y todos resuelven el aggregate por (tenant, usuario, id): un recordatorio ajeno se ve
// como inexistente, nunca como prohibido.
// ---------------------------------------------------------------------------

public sealed record UpdateReminderScheduleCommand(
    Guid TenantId,
    Guid UserId,
    Guid ReminderId,
    DateTime? FireAtUtc,
    DateTime? AnchorAtUtc,
    int? LeadMinutes
);

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
        var schedule =
            command is { AnchorAtUtc: { } anchor, LeadMinutes: { } lead }
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

public sealed record SnoozeReminderCommand(Guid TenantId, Guid UserId, Guid ReminderId, int Minutes);

/// <summary>
/// Posponer solo tiene sentido sobre un recordatorio ya disparado; el aggregate rechaza el resto de
/// estados y aplica el tope de <c>MaxSnoozeCount</c>.
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

public sealed record UpdateReminderSubjectCommand(
    Guid TenantId,
    Guid UserId,
    Guid ReminderId,
    string? Title,
    string? Body
);

/// <summary>Edición de texto: no toca la hora, así que <b>no</b> pasa por el scheduler.</summary>
public static class UpdateReminderSubjectHandler
{
    public static async Task<Result<ReminderResponse>> Handle(
        UpdateReminderSubjectCommand command,
        IReminderRepository reminders,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await reminders.GetOwnedAsync(command.TenantId, command.UserId, command.ReminderId, ct);
        if (found.IsFailure)
            return Result.Failure<ReminderResponse>(found.Error);

        var subject = ReminderSubject.Create(command.Title, command.Body);
        if (subject.IsFailure)
            return Result.Failure<ReminderResponse>(subject.Error);

        var reminder = found.Value;
        var changed = reminder.ChangeSubject(subject.Value);
        if (changed.IsFailure)
            return Result.Failure<ReminderResponse>(changed.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ReminderResponse.From(reminder));
    }
}
