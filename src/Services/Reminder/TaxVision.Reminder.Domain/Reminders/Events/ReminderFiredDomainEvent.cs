using BuildingBlocks.Domain;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Domain.Reminders.Events;

/// <summary>
/// Se emite UNA sola vez aunque Quartz dispare dos (<c>MarkFired</c> es idempotente).
///
/// <para>
/// ⚠️ <b>Reminder no despacha domain events todavía</b> — a diferencia de Auth y Growth, su
/// <c>DbContext.SaveChangesAsync</c> no los drena. El que publica <c>reminder.due.v1</c> es
/// <c>FireReminderHandler</c>, dentro de la misma transacción que el cambio de estado; ahí está
/// explicado por qué. Estos eventos son, por ahora, el registro interno de hechos del aggregate:
/// antes de colgar lógica de uno, hay que construir el despachador.
/// </para>
/// </summary>
public sealed record ReminderFiredDomainEvent(
    Guid ReminderId,
    Guid TenantId,
    Guid UserId,
    ReminderCategory Category,
    DateTime FiredAtUtc
) : IDomainEvent;
