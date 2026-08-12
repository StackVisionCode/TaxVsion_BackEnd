using BuildingBlocks.Domain;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Domain.Reminders.Events;

/// <summary>Metricas. El reagendado en si viaja en <see cref="ReminderRescheduledDomainEvent"/>.</summary>
public sealed record ReminderSnoozedDomainEvent(
    Guid ReminderId,
    Guid TenantId,
    int SnoozeCount,
    DateTime FireAtUtc
) : IDomainEvent;
