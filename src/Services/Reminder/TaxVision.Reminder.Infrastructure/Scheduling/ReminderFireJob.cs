using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using TaxVision.Reminder.Application.Reminders.Commands;
using Wolverine;

namespace TaxVision.Reminder.Infrastructure.Scheduling;

/// <summary>
/// El único job de disparo del servicio. Es <b>durable</b> y no tiene trigger propio: los triggers
/// los crea <see cref="QuartzReminderScheduler"/>, uno por recordatorio.
///
/// <para>
/// <b>No decide nada.</b> Traduce el trigger a <see cref="FireReminderCommand"/> y lo despacha por
/// el bus, igual que un controller despacha cualquier otro comando. Así el disparo y su
/// <c>reminder.due.v1</c> caen dentro de la transacción de Wolverine —outbox durable— en vez de
/// depender de que este proceso siga vivo entre el <c>SaveChanges</c> y la publicación.
/// </para>
///
/// <para>
/// <b><see cref="DisallowConcurrentExecutionAttribute"/></b> serializa las ejecuciones de la misma
/// <c>JobKey</c> en todo el cluster. Con un único job compartido eso significa un disparo a la vez
/// por instancia — aceptable: el trabajo es una lectura y un UPDATE por recordatorio, y el precio de
/// quitarlo sería que dos hilos carguen el mismo aggregate y choquen en <c>RowVersion</c>.
/// </para>
/// </summary>
[DisallowConcurrentExecution]
internal sealed class ReminderFireJob(
    IMessageBus bus,
    IOptions<ReminderSchedulingOptions> options,
    ILogger<ReminderFireJob> logger
) : IJob
{
    internal static readonly JobKey Key = new("reminder-fire", "reminder");

    public async Task Execute(IJobExecutionContext context)
    {
        var data = context.MergedJobDataMap;

        // Con UseProperties = true el JobDataMap solo admite strings — es lo que evita que un cambio
        // de tipos deje triggers sin deserializar en la BD. El precio es parsear acá.
        if (
            !Guid.TryParse(data.GetString(ReminderTriggerKeys.TenantIdKey), out var tenantId)
            || !Guid.TryParse(data.GetString(ReminderTriggerKeys.ReminderIdKey), out var reminderId)
        )
        {
            logger.LogError(
                "Trigger {TriggerKey} carries an unusable JobDataMap; discarding it instead of retrying forever.",
                context.Trigger.Key
            );
            return;
        }

        await bus.InvokeAsync(
            new FireReminderCommand(tenantId, reminderId, options.Value.MisfireGrace),
            context.CancellationToken
        );
    }
}
