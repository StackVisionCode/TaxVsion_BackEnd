using BuildingBlocks.Domain;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Domain.Reminders.Events;

/// <summary>El handler hace <c>DeleteJob</c> en Quartz: un recordatorio cancelado no debe dejar trigger vivo.</summary>
public sealed record ReminderCancelledDomainEvent(
    Guid ReminderId,
    Guid TenantId,
    string Reason,
    DateTime CancelledAtUtc
) : IDomainEvent;
