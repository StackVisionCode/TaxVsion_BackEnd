using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.Reminders;

namespace TaxVision.Reminder.Application.Reminders.Consumers;

/// <summary>
/// Al RETIRAR (offboard) a un empleado: cancela y desagenda sus recordatorios PRIVADOS (Category
/// <c>General</c> — «recordame llamar a Pérez», no apuntan a nada externo). Los atados a
/// Calendar/Task/Note NO se tocan aquí: los cierra el cascade de <c>reminder.target_closed.v1</c> del
/// propio servicio cuando procesa su offboard. Un «recordame a MÍ» no se reasigna: se cancela. Se
/// persiste primero y se desagenda después (EF y Quartz no comparten transacción; el job de
/// reconciliación cubre el hueco). Idempotente.
/// </summary>
public static class ReminderUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        IReminderRepository reminders,
        IReminderScheduler scheduler,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        IReminderMetrics metrics,
        ILogger<ReminderAggregate> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId
            )
        )
        {
            var pending = await reminders.ListPendingGeneralByUserAsync(msg.TenantId, msg.UserId, ct);
            if (pending.Count == 0)
                return;

            var nowUtc = DateTime.UtcNow;
            var cancelled = pending
                .Where(reminder => reminder.Cancel(ReminderCancellationReasons.OwnerOffboarded, nowUtc).IsSuccess)
                .ToList();

            if (cancelled.Count == 0)
                return;

            await unitOfWork.SaveChangesAsync(ct);

            foreach (var reminder in cancelled)
            {
                await scheduler.UnscheduleAsync(reminder.TenantId, reminder.Id, ct);
                metrics.RecordCancelled(ReminderCancellationReasons.OwnerOffboarded);
            }

            logger.LogInformation(
                "Offboarded user {UserId}: cancelled and unscheduled {Count} private reminder(s) in tenant {TenantId}.",
                msg.UserId,
                cancelled.Count,
                msg.TenantId
            );
        }
    }
}
