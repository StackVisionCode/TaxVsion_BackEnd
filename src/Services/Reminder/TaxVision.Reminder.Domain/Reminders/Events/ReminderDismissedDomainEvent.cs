using BuildingBlocks.Domain;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Domain.Reminders.Events;

/// <summary>Metricas — el usuario lo vio y lo cerro.</summary>
public sealed record ReminderDismissedDomainEvent(Guid ReminderId, Guid TenantId, DateTime DismissedAtUtc)
    : IDomainEvent;
