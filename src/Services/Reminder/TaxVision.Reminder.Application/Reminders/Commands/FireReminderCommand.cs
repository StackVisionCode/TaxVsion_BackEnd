using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.Reminders;
using Wolverine;

namespace TaxVision.Reminder.Application.Reminders.Commands;

/// <param name="MisfireGrace">
/// Tolerancia configurada (<c>Reminder:MisfireGraceMinutes</c>). Viaja en el comando porque es la
/// tolerancia del <b>scheduler</b>, y el scheduler es quien la tiene configurada; la <b>decisión</b>
/// de descartar el aviso sigue siendo del aggregate (<c>FireOrMiss</c>), no de este handler.
/// </param>
public sealed record FireReminderCommand(Guid TenantId, Guid ReminderId, TimeSpan MisfireGrace);

/// <summary>
/// El disparo. Lo invoca el job de Quartz vía <c>bus.InvokeAsync</c>, igual que un controller invoca
/// cualquier otro comando: el job aporta el «cuándo», este handler el «qué».
///
/// <para>
/// <b>Premisa caída del plan.</b> El plan decía publicar <c>reminder.due.v1</c> «desde el handler de
/// <c>ReminderFiredDomainEvent</c>», dando por hecho un despachador de domain events que Reminder
/// nunca tuvo (Auth y Growth sí lo tienen, en su <c>DbContext.SaveChangesAsync</c>). Medido, no
/// asumido: ningún servicio del monorepo llama <c>UseDurableLocalQueues()</c>, así que esa ruta
/// entrega el domain event por una cola <b>en memoria</b> — un reinicio entre el <c>SaveChanges</c>
/// y el handler dejaría el recordatorio en <c>Fired</c> sin que el aviso saliera nunca, y nada lo
/// repararía. Publicar el integration event acá lo mete en la <b>misma transacción</b> que el cambio
/// de estado (outbox durable de Wolverine): o se persiste el disparo y sale el evento, o ninguna
/// de las dos cosas.
/// </para>
///
/// <para>
/// <b>Un solo evento por recordatorio.</b> <c>MarkFired</c> es idempotente y devuelve éxito sin
/// re-emitir si ya estaba <c>Fired</c> — en un failover de cluster Quartz puede ejecutar el mismo
/// trigger dos veces. Por eso se compara el estado <b>anterior</b>: publicar según el estado final
/// avisaría al usuario dos veces del mismo recordatorio.
/// </para>
/// </summary>
public static class FireReminderHandler
{
    public static async Task Handle(
        FireReminderCommand command,
        IReminderRepository reminders,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IReminderMetrics metrics,
        ILogger<ReminderAggregate> logger,
        CancellationToken ct
    )
    {
        var found = await reminders.GetForSchedulerAsync(command.TenantId, command.ReminderId, ct);
        if (found.IsFailure)
        {
            // El recordatorio se borró y el trigger sobrevivió. No es reintentable: reintentar lo
            // dejaría disparando cada pocos segundos para siempre.
            logger.LogWarning(
                "Reminder {ReminderId} of tenant {TenantId} no longer exists; its trigger will not be retried.",
                command.ReminderId,
                command.TenantId
            );
            return;
        }

        var reminder = found.Value;
        var nowUtc = DateTime.UtcNow;
        var delay = nowUtc - reminder.Schedule.FireAtUtc;
        var statusBeforeFiring = reminder.Status;

        var result = reminder.FireOrMiss(nowUtc, command.MisfireGrace);
        if (result.IsFailure)
        {
            // Llegó a un estado terminal por otra vía (cancelado, descartado) entre que Quartz eligió
            // el trigger y esta ejecución. El trigger sobrante es basura, no un fallo.
            logger.LogInformation(
                "Reminder {ReminderId} of tenant {TenantId} is in status {Status}; nothing to fire ({ErrorCode}).",
                command.ReminderId,
                command.TenantId,
                reminder.Status,
                result.Error.Code
            );
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);

        // Se mide contra el estado ANTERIOR por el mismo motivo que se publica contra él: en un
        // failover de cluster Quartz ejecuta el trigger dos veces, y contar el segundo pase
        // duplicaría el disparo en el dashboard sin que hubiera ocurrido dos veces.
        if (statusBeforeFiring != reminder.Status)
        {
            if (reminder.Status == ReminderStatus.Fired)
            {
                await bus.PublishAsync(BuildDueEvent(reminder, correlation.CorrelationId));
                metrics.RecordFired(reminder.Target.Category);
                metrics.RecordFireDelaySeconds(delay.TotalSeconds);
            }
            else if (reminder.Status == ReminderStatus.Missed)
            {
                metrics.RecordMisfired(ReminderMisfirePolicies.GraceExceeded);
            }
        }

        logger.LogInformation(
            "Reminder {ReminderId} of tenant {TenantId} resolved to {Status} with a delay of {DelaySeconds:F0}s "
                + "(grace {GraceMinutes:F0} min).",
            command.ReminderId,
            command.TenantId,
            reminder.Status,
            delay.TotalSeconds,
            command.MisfireGrace.TotalMinutes
        );
    }

    private static ReminderDueIntegrationEvent BuildDueEvent(ReminderAggregate reminder, string correlationId) =>
        new()
        {
            TenantId = reminder.TenantId,
            CorrelationId = correlationId,
            ReminderId = reminder.Id,
            UserId = reminder.UserId,
            Category = reminder.Target.Category.ToString(),
            TargetId = reminder.Target.TargetId,
            Title = reminder.Subject.Title,
            Body = reminder.Subject.Body,
            TimeZoneId = reminder.TimeZone.Value,
            AnchorAtUtc = reminder.Schedule.AnchorAtUtc,
            FiredAtUtc = reminder.FiredAtUtc ?? DateTime.UtcNow,
            SnoozeCount = reminder.SnoozeCount,
        };
}
