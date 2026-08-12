using BuildingBlocks.Domain;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Domain.Reminders.Events;

/// <summary>
/// El handler hace <c>RescheduleJob</c> en Quartz. Lo emiten las tres formas de mover un disparo:
/// reaccion a <c>target_moved</c>, edicion explicita del usuario y snooze.
/// </summary>
public sealed record ReminderRescheduledDomainEvent(
    Guid ReminderId,
    Guid TenantId,
    DateTime PreviousFireAtUtc,
    DateTime FireAtUtc
) : IDomainEvent;
