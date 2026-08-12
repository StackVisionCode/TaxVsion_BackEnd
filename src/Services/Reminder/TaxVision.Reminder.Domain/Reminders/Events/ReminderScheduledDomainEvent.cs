using BuildingBlocks.Domain;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Domain.Reminders.Events;

/// <summary>
/// El recordatorio nació con hora. Quien crea el trigger en Quartz es <c>CreateReminderHandler</c>,
/// después de persistir (ADR-R-04) — ver la nota de <see cref="ReminderFiredDomainEvent"/> sobre
/// por qué el servicio todavía no despacha domain events.
/// </summary>
public sealed record ReminderScheduledDomainEvent(
    Guid ReminderId,
    Guid TenantId,
    Guid UserId,
    ReminderCategory Category,
    DateTime FireAtUtc
) : IDomainEvent;
