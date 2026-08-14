using BuildingBlocks.Common;
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

    /// <summary>
    /// Mapea una página conservando <c>Page</c>/<c>Size</c>/<c>TotalCount</c> del repositorio. Vive
    /// acá y no en un handler porque lo usan los dos listados: dejarlo en uno obligaba al otro a
    /// llamarlo, y eso es lo que ataba <c>ListUpcomingRemindersHandler</c> a
    /// <c>ListMyRemindersHandler</c> sin ninguna relación de negocio entre ellos.
    /// </summary>
    public static PagedResult<ReminderResponse> FromPage(PagedResult<ReminderAggregate> page) =>
        new(page.Items.Select(From).ToList(), page.Page, page.Size, page.TotalCount);
}
