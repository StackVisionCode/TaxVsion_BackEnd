using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Infrastructure.Persistence;

namespace TaxVision.Reminder.Infrastructure.Scheduling;

/// <summary>
/// Purga los recordatorios que ya terminaron (<c>Dismissed</c>/<c>Cancelled</c>/<c>Missed</c>) y
/// llevan más de <c>Reminder:RetentionMonths</c> resueltos. No es limpieza cosmética: la tabla solo
/// crece, y el índice de listados por <c>(TenantId, UserId, Status)</c> se degrada arrastrando filas
/// que ninguna consulta del producto vuelve a mirar.
///
/// <para>
/// <b>Se filtra por <c>ResolvedAtUtc</c>, no por <c>CreatedAtUtc</c>.</b> Un recordatorio creado hace
/// dos años pero cancelado ayer es reciente para soporte; lo que define la antigüedad es cuándo
/// terminó, que es justo el campo que el aggregate escribe al llegar a un estado terminal.
/// </para>
///
/// <para>
/// <b><c>IgnoreQueryFilters()</c> es obligatorio</b> — mismo motivo exacto que
/// <see cref="ReminderScheduleReconciliationJob"/>: no hay tenant en contexto, el filtro global es
/// fail-closed, y sin esto la consulta devuelve <b>0 filas siempre</b> mientras el job se ve sano en
/// los logs.
/// </para>
///
/// <para>
/// Borra en lotes con <c>ExecuteDeleteAsync</c> y con tope por corrida: un primer barrido sobre una
/// tabla vieja podría tocar cientos de miles de filas, y un solo DELETE de ese tamaño bloquea la
/// tabla el tiempo suficiente como para afectar a los disparos que están ocurriendo en paralelo.
/// </para>
/// </summary>
public sealed class ReminderRetentionJob(
    IServiceScopeFactory scopeFactory,
    IOptions<ReminderSchedulingOptions> options,
    ILogger<ReminderRetentionJob> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private const int BatchSize = 500;
    private const int MaxBatchesPerRun = 20;

    private static readonly ReminderStatus[] TerminalStatuses =
    [
        ReminderStatus.Dismissed,
        ReminderStatus.Cancelled,
        ReminderStatus.Missed,
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reminder retention purge failed; will retry next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Pública a propósito: es la unidad de trabajo real del job y la única forma de probarla es
    /// invocarla. Encerrada en el <c>ExecuteAsync</c> solo se podría verificar con un test que
    /// esperara 24 horas.
    /// </summary>
    public async Task PurgeAsync(CancellationToken ct)
    {
        var months = options.Value.RetentionMonths;
        if (months <= 0)
            return;

        var cutoffUtc = DateTime.UtcNow.AddMonths(-months);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReminderDbContext>();

        var deleted = 0;
        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            var removed = await db
                .Reminders.IgnoreQueryFilters()
                .Where(r =>
                    TerminalStatuses.Contains(r.Status) && r.ResolvedAtUtc != null && r.ResolvedAtUtc < cutoffUtc
                )
                .OrderBy(r => r.ResolvedAtUtc)
                .Take(BatchSize)
                .ExecuteDeleteAsync(ct);

            deleted += removed;
            if (removed < BatchSize)
                break;
        }

        if (deleted > 0)
            logger.LogInformation(
                "Reminder retention purged {Deleted} terminal reminder(s) resolved before {CutoffUtc:O} "
                    + "({RetentionMonths} month(s) of retention).",
                deleted,
                cutoffUtc,
                months
            );
    }
}
