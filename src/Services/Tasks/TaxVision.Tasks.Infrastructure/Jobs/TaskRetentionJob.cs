using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.Jobs;

/// <summary>
/// Purga las referencias a archivos que ya se desadjuntaron hace mucho.
///
/// <para>
/// <b>Purga referencias, no tareas.</b> Una tarea cerrada es el registro de un encargo fiscal y se
/// conserva: quien pregunta en octubre quién preparó ese 1040 necesita encontrarlo. Lo que no aporta
/// nada es la fila de un adjunto que alguien quitó hace un año —el archivo lo recoge la retención de
/// CloudStorage, que es su dueño—.
/// </para>
///
/// <para>
/// <b><c>IgnoreQueryFilters()</c> es obligatorio</b>: no hay tenant en contexto y el filtro global es
/// fail-closed, así que sin esto la consulta devuelve 0 filas siempre mientras el job se ve sano en
/// los logs.
/// </para>
/// </summary>
internal sealed class TaskRetentionJob(
    IServiceScopeFactory scopeFactory,
    IOptions<TaskRetentionOptions> options,
    ILogger<TaskRetentionJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(settings.IntervalHours));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PurgeAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TaskRetentionJob failed; retrying on the next tick.");
            }
        }
    }

    private async Task PurgeAsync(TaskRetentionOptions settings, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var cutoff = DateTime.UtcNow.AddMonths(-settings.DetachedAttachmentMonths);

        // En lotes: un primer barrido sobre una tabla vieja podría tocar cientos de miles de filas, y
        // un solo DELETE de ese tamaño bloquea la tabla mientras el producto la está usando.
        var deleted = await context
            .Set<TaskAttachment>()
            .IgnoreQueryFilters()
            .Where(a => a.Status == AttachmentStatus.Detached && a.DetachedAtUtc != null && a.DetachedAtUtc < cutoff)
            .Take(settings.BatchSize)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            logger.LogInformation("TaskRetentionJob removed {Count} detached attachment reference(s).", deleted);
    }
}
