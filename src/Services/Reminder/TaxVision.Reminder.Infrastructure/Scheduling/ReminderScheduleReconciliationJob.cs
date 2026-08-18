using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Reminder.Application.Reminders.Abstractions;

namespace TaxVision.Reminder.Infrastructure.Scheduling;

/// <summary>
/// Red de seguridad del ADR-R-04. EF y Quartz no comparten transacción y el orden elegido es EF
/// primero: un recordatorio puede quedar <c>Scheduled</c> en la BD sin trigger vivo si el proceso
/// muere entre <c>SaveChanges</c> y <c>ScheduleJob</c>, o si alguien toca <c>QRTZ_TRIGGERS</c> a
/// mano. Sin este barrido ese recordatorio no dispara nunca y nadie se entera.
///
/// <para>
/// <b>El detalle que hace o rompe este job es <c>IgnoreQueryFilters()</c></b> dentro de
/// <c>ListScheduledWithinHorizonAsync</c>: acá no hay tenant en contexto, el filtro global es
/// fail-closed y sin él la consulta devuelve <b>0 filas siempre</b> — el job se ve perfectamente
/// sano en los logs mientras no repara nada. Es el mismo bug que ya se evitó en
/// <c>CodeReservationRepository.GetActiveExpiredAsync</c> de Growth.
/// </para>
///
/// <para>
/// Reagendar es idempotente (<c>ScheduleAsync</c> reemplaza), así que dos réplicas barriendo a la
/// vez no se pisan.
/// </para>
/// </summary>
public sealed class ReminderScheduleReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IReminderScheduler scheduler,
    IOptions<ReminderSchedulingOptions> options,
    ILogger<ReminderScheduleReconciliationJob> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reminder schedule reconciliation failed; will retry next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var reminders = scope.ServiceProvider.GetRequiredService<IReminderRepository>();

        var horizonUtc = DateTime.UtcNow.Add(options.Value.ReconciliationHorizon);
        var pending = await reminders.ListScheduledWithinHorizonAsync(horizonUtc, ct);
        if (pending.Count == 0)
            return;

        var repaired = 0;
        foreach (var reminder in pending)
        {
            if (await scheduler.IsScheduledAsync(reminder.TenantId, reminder.Id, ct))
                continue;

            await scheduler.ScheduleAsync(reminder.TenantId, reminder.Id, reminder.Schedule.FireAtUtc, ct);
            repaired++;

            logger.LogWarning(
                "Reminder {ReminderId} of tenant {TenantId} was Scheduled without a live trigger; rescheduled for {FireAtUtc:O}.",
                reminder.Id,
                reminder.TenantId,
                reminder.Schedule.FireAtUtc
            );
        }

        if (repaired > 0)
            logger.LogInformation(
                "Reminder schedule reconciliation repaired {Repaired}/{Total} reminder(s) within the horizon.",
                repaired,
                pending.Count
            );
    }
}
