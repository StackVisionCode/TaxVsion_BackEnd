using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Application.Reminders;

/// <summary>
/// Proyección de salida del aggregate. Aplana los VOs a propósito: el contrato HTTP no debe cambiar
/// porque un VO se reorganice por dentro.
/// </summary>
public sealed record ReminderResponse(
    Guid Id,
    Guid UserId,
    string Title,
    string? Body,
    ReminderCategory Category,
    Guid? TargetId,
    DateTime FireAtUtc,
    DateTime? AnchorAtUtc,
    int? LeadMinutes,
    string TimeZone,
    ReminderStatus Status,
    string RequestKey,
    DateTime CreatedAtUtc,
    DateTime? FiredAtUtc,
    DateTime? ResolvedAtUtc,
    string? CancellationReason,
    int SnoozeCount
)
{
    public static ReminderResponse From(ReminderAggregate reminder) =>
        new(
            reminder.Id,
            reminder.UserId,
            reminder.Subject.Title,
            reminder.Subject.Body,
            reminder.Target.Category,
            reminder.Target.TargetId,
            reminder.Schedule.FireAtUtc,
            reminder.Schedule.AnchorAtUtc,
            reminder.Schedule.LeadMinutes,
            reminder.TimeZone.Value,
            reminder.Status,
            reminder.RequestKey.Value,
            reminder.CreatedAtUtc,
            reminder.FiredAtUtc,
            reminder.ResolvedAtUtc,
            reminder.CancellationReason,
            reminder.SnoozeCount
        );
}
