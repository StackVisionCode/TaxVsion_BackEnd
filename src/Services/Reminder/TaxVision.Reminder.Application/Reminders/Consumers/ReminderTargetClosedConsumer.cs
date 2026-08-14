using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.Reminders;

namespace TaxVision.Reminder.Application.Reminders.Consumers;

// Resuelve por (tenant, categoría, targetId) — nunca por un ID de recordatorio, que el publicador no
// conoce — y guarda UNA vez para todos los afectados, reagendando en Quartz después de persistir
// (ADR-R-04). Sin este consumer el servicio avisa de tareas que ya se hicieron, que es exactamente
// la forma en que un sistema de recordatorios pierde la confianza del usuario.

/// <summary>
/// <c>reminder.target_closed.v1</c> — el objetivo se completó, se canceló o se borró.
///
/// <para>
/// La razón de cancelación es siempre <c>target_closed</c>, no el <c>Reason</c> del evento: ese
/// describe qué le pasó al <b>objetivo</b>, y mezclarlo haría imposible distinguir en soporte «lo
/// canceló el usuario» de «se cerró aquello a lo que apuntaba», que es justo para lo que existe el
/// campo (invariante del aggregate).
/// </para>
/// </summary>
public static class ReminderTargetClosedConsumer
{
    public static async Task Handle(
        ReminderTargetClosedIntegrationEvent evt,
        IReminderRepository reminders,
        IReminderScheduler scheduler,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        IReminderMetrics metrics,
        ILogger<ReminderAggregate> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ReminderInboundEvent.CorrelationOf(evt)))
        {
            if (!ReminderInboundEvent.TryParseCategory(evt.Category, out var category))
            {
                ReminderInboundEvent.LogUnknownCategory(logger, evt.Category, evt.EventId, "reminder.target_closed.v1");
                return;
            }

            var affected = await reminders.ListPendingByTargetAsync(evt.TenantId, category, evt.TargetId, ct);
            if (affected.Count == 0)
                return;

            var nowUtc = DateTime.UtcNow;
            var cancelled = affected
                .Where(reminder => reminder.Cancel(ReminderCancellationReasons.TargetClosed, nowUtc).IsSuccess)
                .ToList();

            if (cancelled.Count == 0)
                return;

            await unitOfWork.SaveChangesAsync(ct);

            foreach (var reminder in cancelled)
            {
                await scheduler.UnscheduleAsync(reminder.TenantId, reminder.Id, ct);
                metrics.RecordCancelled(ReminderCancellationReasons.TargetClosed);
            }

            logger.LogInformation(
                "reminder.target_closed.v1 cancelled {CancelledCount} reminders of target {TargetId} "
                    + "(target reason: {TargetReason}).",
                cancelled.Count,
                evt.TargetId,
                evt.Reason ?? "unspecified"
            );
        }
    }
}
