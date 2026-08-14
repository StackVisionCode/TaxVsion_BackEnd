using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Infrastructure.Jobs;

/// <summary>
/// Publica <c>task.overdue.v1</c> por cada tarea que pasó su vencimiento y sigue abierta.
///
/// <para>
/// <b>Un aviso por vencimiento, no por barrido.</b> La tarea seguirá vencida mañana; sin la marca
/// <see cref="TaskItem.OverdueNotifiedAtUtc"/> el asignado recibiría el mismo aviso cada hora hasta
/// silenciar el canal —y entonces deja de ver también los que sí importan—. Mover la fecha limpia la
/// marca, así que un vencimiento nuevo vuelve a avisar.
/// </para>
/// </summary>
internal sealed class OverdueTaskSweepJob(
    IServiceScopeFactory scopeFactory,
    IOptions<OverdueTaskSweepOptions> options,
    ILogger<OverdueTaskSweepJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // La pasada de arranque publica eventos, y Wolverine todavía no está listo cuando el host
        // levanta los hosted services: sin esta espera lanza WolverineHasNotStartedException y la
        // primera corrida se pierde entera.
        await scopeFactory
            .CreateScope()
            .ServiceProvider.GetRequiredService<IHostApplicationLifetime>()
            .WaitForApplicationStartedAsync(stoppingToken);

        var settings = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.IntervalMinutes));

        // Una pasada al arrancar: si el servicio estuvo caído, hay vencimientos esperando aviso.
        do
        {
            try
            {
                await SweepAsync(settings.BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OverdueTaskSweepJob failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Trozos chicos y <b>guardar antes de publicar</b>. Otro job del servicio corrige contadores
    /// sobre estas mismas filas, así que el <c>rowversion</c> cambia bajo los pies del barrido: en un
    /// lote grande una sola colisión tira el guardado entero. Publicar primero dejaría los avisos
    /// fuera y la marca sin escribir — el aviso repetido que la marca existe para impedir.
    /// </summary>
    private const int ChunkSize = 25;

    private async Task SweepAsync(int batchSize, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var announced = 0;

        // La consulta excluye las ya marcadas, así que cada vuelta trae las siguientes. El tope de
        // vueltas evita quedarse girando sobre un trozo que colisiona siempre.
        for (var round = 0; round < batchSize / ChunkSize && !ct.IsCancellationRequested; round++)
        {
            var done = await AnnounceChunkAsync(now, ct);
            if (done == 0)
                break;

            announced += done;
        }

        if (announced > 0)
            logger.LogInformation("OverdueTaskSweepJob announced {Announced} overdue task(s).", announced);
    }

    private async Task<int> AnnounceChunkAsync(DateTime now, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
        var metrics = scope.ServiceProvider.GetRequiredService<ITaskMetrics>();

        var overdue = await tasks.ListOverdueAsync(now, ChunkSize, ct);
        var marked = overdue.Where(task => task.MarkOverdueNotified(now)).ToList();
        if (marked.Count == 0)
            return 0;

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Alguien tocó una de estas filas mientras tanto. Siguen sin marca, así que el barrido
            // siguiente las recoge; avisar ahora sería avisar dos veces.
            logger.LogDebug("OverdueTaskSweepJob skipped {Count} task(s) modified concurrently.", marked.Count);
            return 0;
        }

        foreach (var task in marked)
            await bus.PublishAsync(BuildEvent(task, correlation.CorrelationId));

        metrics.RecordOverdue(marked.Count);
        return marked.Count;
    }

    private static TaskOverdueIntegrationEvent BuildEvent(TaskItem task, string correlationId) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            TaskId = task.Id,
            Title = task.Title.Value,
            DueAtUtc = task.Due!.DueAtUtc,
            IsStatutory = task.Due.IsStatutory,
            AssigneeUserId = task.AssigneeUserId,
            CustomerId = task.Reference.CustomerId,
            TaxYear = task.Reference.TaxYear,
        };
}
