using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.Reminders;

namespace TaxVision.Reminder.Application.Reminders.Consumers;

// ---------------------------------------------------------------------------
// Los dos eventos que mantienen los recordatorios sincronizados con su objetivo. Sin ellos el
// servicio avisa de citas que se movieron y de tareas que ya se hicieron, que es exactamente la
// forma en que un sistema de recordatorios pierde la confianza del usuario.
//
// Ambos resuelven por (tenant, categoría, targetId) — nunca por un ID de recordatorio, que el
// publicador no conoce — y guardan UNA vez para todos los afectados: cada uno reagenda o desagenda
// en Quartz después de persistir (ADR-R-04).
// ---------------------------------------------------------------------------

/// <summary>
/// <c>reminder.target_moved.v1</c> — el objetivo cambió de fecha.
///
/// <para>
/// Los recordatorios <b>absolutos</b> son un <b>no-op exitoso</b> (invariante R6): el usuario dijo
/// «el jueves a las 9 pase lo que pase». <c>RescheduleToNewAnchor</c> ya devuelve éxito sin tocar
/// nada en ese caso, así que acá se detecta por «la hora no cambió» y no se molesta a Quartz.
/// </para>
/// </summary>
public static class ReminderTargetMovedConsumer
{
    public static async Task Handle(
        ReminderTargetMovedIntegrationEvent evt,
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
                ReminderInboundEvent.LogUnknownCategory(logger, evt.Category, evt.EventId, "reminder.target_moved.v1");
                return;
            }

            var affected = await reminders.ListPendingByTargetAsync(evt.TenantId, category, evt.TargetId, ct);
            if (affected.Count == 0)
                return;

            var nowUtc = DateTime.UtcNow;
            var moved = new List<(ReminderAggregate Reminder, bool StillPending)>();

            foreach (var reminder in affected)
            {
                var previousFireAtUtc = reminder.Schedule.FireAtUtc;
                var result = reminder.RescheduleToNewAnchor(evt.NewAnchorAtUtc, nowUtc);

                if (result.IsFailure)
                {
                    // Ya disparó y espera al usuario, o llegó a un estado terminal. Mover su hora
                    // ahora sería reabrir un aviso que la persona ya vio.
                    logger.LogInformation(
                        "Reminder {ReminderId} was not moved: it is {Status} ({ErrorCode}).",
                        reminder.Id,
                        reminder.Status,
                        result.Error.Code
                    );
                    continue;
                }

                if (reminder.Schedule.FireAtUtc != previousFireAtUtc)
                    moved.Add((reminder, reminder.Status == ReminderStatus.Scheduled));
            }

            if (moved.Count == 0)
                return;

            await unitOfWork.SaveChangesAsync(ct);

            foreach (var (reminder, stillPending) in moved)
            {
                // La hora recalculada podía haber quedado en el pasado: el aggregate lo resolvió como
                // Missed y lo que corresponde entonces es sacar el trigger, no moverlo.
                if (stillPending)
                {
                    await scheduler.RescheduleAsync(reminder.TenantId, reminder.Id, reminder.Schedule.FireAtUtc, ct);
                }
                else
                {
                    await scheduler.UnscheduleAsync(reminder.TenantId, reminder.Id, ct);
                    metrics.RecordMisfired(ReminderMisfirePolicies.AnchorMovedToPast);
                }
            }

            logger.LogInformation(
                "reminder.target_moved.v1 moved {MovedCount} of {CandidateCount} reminders of target {TargetId}.",
                moved.Count,
                affected.Count,
                evt.TargetId
            );
        }
    }
}

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
