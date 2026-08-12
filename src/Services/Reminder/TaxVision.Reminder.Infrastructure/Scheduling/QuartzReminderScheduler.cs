using Microsoft.Extensions.Logging;
using Quartz;
using TaxVision.Reminder.Application.Reminders.Abstractions;

namespace TaxVision.Reminder.Infrastructure.Scheduling;

/// <summary>
/// Adaptador del puerto <see cref="IReminderScheduler"/>. Es el <b>único</b> tipo del servicio que
/// habla con <c>IScheduler</c>; el resto pasa por el puerto (verificado por NetArchTest).
///
/// <para>
/// Todos los triggers apuntan al mismo <c>ReminderFireJob</c>, que es durable: no hay una clase de
/// job por recordatorio. Lo que distingue una ejecución de otra es el <c>JobDataMap</c>
/// (tenantId + reminderId), que con <c>UseProperties = true</c> solo lleva strings.
/// </para>
/// </summary>
internal sealed class QuartzReminderScheduler(
    ISchedulerFactory schedulerFactory,
    ILogger<QuartzReminderScheduler> logger
) : IReminderScheduler
{
    public async Task ScheduleAsync(Guid tenantId, Guid reminderId, DateTime fireAtUtc, CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var trigger = BuildTrigger(tenantId, reminderId, fireAtUtc);

        // replace: true hace la operación idempotente — reagendar el mismo recordatorio sustituye el
        // trigger en vez de reventar con ObjectAlreadyExistsException. Sin esto la reconciliación no
        // podría reintentar.
        await scheduler.ScheduleJob(trigger, ct);

        logger.LogDebug(
            "Scheduled reminder {ReminderId} of tenant {TenantId} for {FireAtUtc:O}.",
            reminderId,
            tenantId,
            fireAtUtc
        );
    }

    public Task RescheduleAsync(
        Guid tenantId,
        Guid reminderId,
        DateTime newFireAtUtc,
        CancellationToken ct = default
    ) => ReplaceAsync(tenantId, reminderId, newFireAtUtc, ct);

    public async Task UnscheduleAsync(Guid tenantId, Guid reminderId, CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);

        // Devuelve false si no existía: no es un error. Cancelar un recordatorio cuyo trigger ya
        // disparó (o que la reconciliación nunca llegó a crear) tiene que ser un no-op.
        var removed = await scheduler.UnscheduleJob(ReminderTriggerKeys.For(tenantId, reminderId), ct);

        logger.LogDebug(
            "Unschedule of reminder {ReminderId} of tenant {TenantId} — trigger existed: {Existed}.",
            reminderId,
            tenantId,
            removed
        );
    }

    public async Task<bool> IsScheduledAsync(Guid tenantId, Guid reminderId, CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        return await scheduler.CheckExists(ReminderTriggerKeys.For(tenantId, reminderId), ct);
    }

    private async Task ReplaceAsync(Guid tenantId, Guid reminderId, DateTime fireAtUtc, CancellationToken ct)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var key = ReminderTriggerKeys.For(tenantId, reminderId);
        var trigger = BuildTrigger(tenantId, reminderId, fireAtUtc);

        // RescheduleJob sobre una clave inexistente devuelve null y NO agenda nada — pasa cuando el
        // recordatorio se creó mientras Quartz estaba caído. Se cae a ScheduleJob para que
        // reprogramar sirva también como reparación.
        var next = await scheduler.RescheduleJob(key, trigger, ct);
        if (next is null)
        {
            await scheduler.ScheduleJob(trigger, ct);
            logger.LogInformation(
                "Reminder {ReminderId} of tenant {TenantId} had no live trigger; scheduled instead of rescheduled.",
                reminderId,
                tenantId
            );
        }
    }

    private static ITrigger BuildTrigger(Guid tenantId, Guid reminderId, DateTime fireAtUtc) =>
        TriggerBuilder
            .Create()
            .WithIdentity(ReminderTriggerKeys.For(tenantId, reminderId))
            .StartAt(new DateTimeOffset(DateTime.SpecifyKind(fireAtUtc, DateTimeKind.Utc)))
            // Quartz SIEMPRE dispara al recuperarse de un misfire; es el aggregate quien decide si el
            // aviso sigue vigente comparando el retraso real contra MisfireGraceMinutes (02_ §4.4).
            // Expresar esa regla acá sería meter negocio en la infraestructura.
            .WithSimpleSchedule(x => x.WithMisfireHandlingInstructionFireNow())
            .UsingJobData(ReminderTriggerKeys.TenantIdKey, tenantId.ToString())
            .UsingJobData(ReminderTriggerKeys.ReminderIdKey, reminderId.ToString())
            .ForJob(ReminderFireJob.Key)
            .Build();
}
