using BuildingBlocks.Domain;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Domain.Reminders.Events;

/// <summary>
/// Alimenta <c>reminder.misfired_total</c> — la metrica que delata que el servicio estuvo caido y se
/// descartaron avisos. Lo emiten dos caminos: la politica de misfire del job, y un
/// <c>target_moved</c> cuyo disparo recalculado ya paso.
/// </summary>
public sealed record ReminderMissedDomainEvent(
    Guid ReminderId,
    Guid TenantId,
    DateTime ExpectedFireAtUtc,
    DateTime MissedAtUtc
) : IDomainEvent;
