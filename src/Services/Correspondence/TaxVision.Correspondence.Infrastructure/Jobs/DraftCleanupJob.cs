using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Infrastructure.Observability;

namespace TaxVision.Correspondence.Infrastructure.Jobs;

/// <summary>
/// Cada 24h busca <c>Draft</c>s en <c>Status=Draft</c> cuyo <c>UpdatedAtUtc</c> es anterior a
/// <c>AbandonedAfterDays</c> (default 30, plan §30) y los pasa a <c>Discarded</c> vía
/// <see cref="Domain.Compose.Draft.Discard"/> — nunca un update SQL crudo: es el mismo aggregate
/// method que usa <c>DiscardDraftHandler</c>, así que la invariante de estado (solo válido desde
/// <c>Draft</c>) se respeta igual acá. Deshabilitado por default (<see cref="DraftCleanupOptions.Enabled"/>).
///
/// <para>
/// Job HTTP-triggered NO, es timer-tick puro sin evento de entrada — no hay correlación inbound
/// que propagar (guardrail de background jobs de este servicio). Aun así empuja una correlación
/// NUEVA por batch (no por draft — a diferencia de <c>WatchRenewalJob</c> de Connectors, que sí
/// aísla por item porque cada renewal es una llamada de red independiente; acá todo un batch se
/// guarda con un solo <c>SaveChangesAsync</c>) para que las líneas de log de esa pasada queden
/// agrupables, mismo mecanismo que <c>ICorrelationContext.Push</c> ya usa en el resto del repo.
/// </para>
/// </summary>
public sealed class DraftCleanupJob(IServiceProvider serviceProvider, ILogger<DraftCleanupJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private const int MaxBatchesPerRun = 25;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceSafeAsync(stoppingToken);
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceSafeAsync(CancellationToken ct)
    {
        try
        {
            await RunOnceAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DraftCleanupJob iteration failed.");
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var optionsScope = serviceProvider.CreateScope();
        var options = optionsScope.ServiceProvider.GetRequiredService<IOptions<DraftCleanupOptions>>().Value;
        if (!options.Enabled)
            return;

        var cutoff = DateTime.UtcNow.AddDays(-options.AbandonedAfterDays);
        var totalDiscarded = 0;

        // Batches acotados por corrida — mismo criterio que ConnectorsRetentionScheduler: evita
        // retener el scope/conexión abiertos indefinidamente ante un backlog grande. El corte del
        // loop mira cuántas filas trajo la QUERY (fetched), no cuántas se descartaron con éxito
        // (discarded) — si algún Draft.Discard() individual fallara (invariante de carrera con un
        // request concurrente que lo movió a Sending justo entre el query y el save), un batch
        // "fetched == BatchSize pero discarded < BatchSize" no debe cortar el loop antes de tiempo:
        // esa fila sigue matcheando el query (Status sigue Draft si falló) y hay que seguir
        // avanzando sobre el resto en vez de reprocesarla en un loop infinito de un solo elemento.
        // MaxBatchesPerRun acota un backlog enorme: el resto se limpia en la corrida siguiente
        // (24h después), mismo trade-off que ConnectorsRetentionScheduler.
        for (var batchNumber = 0; batchNumber < MaxBatchesPerRun && !ct.IsCancellationRequested; batchNumber++)
        {
            var (fetched, discarded) = await DiscardOneBatchAsync(cutoff, options.BatchSize, ct);
            totalDiscarded += discarded;
            if (fetched < options.BatchSize)
                break;
        }

        if (totalDiscarded > 0)
            logger.LogInformation(
                "DraftCleanupJob discarded {Count} abandoned drafts older than {Cutoff}.",
                totalDiscarded,
                cutoff
            );
    }

    private async Task<(int Fetched, int Discarded)> DiscardOneBatchAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken ct
    )
    {
        using var scope = serviceProvider.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDraftRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        var batch = await drafts.ListAbandonedAsync(cutoff, batchSize, ct);
        if (batch.Count == 0)
            return (0, 0);

        using (correlation.Push(Guid.NewGuid().ToString("N")))
        {
            var discarded = 0;
            foreach (var draft in batch)
            {
                var result = draft.Discard();
                if (result.IsFailure)
                {
                    logger.LogWarning(
                        "DraftCleanupJob could not discard draft {DraftId}: {Error}",
                        draft.Id,
                        result.Error.Message
                    );
                    continue;
                }

                discarded++;
                CorrespondenceMetrics.DraftAbandonedTotal.Add(
                    1,
                    new KeyValuePair<string, object?>("tenant", draft.TenantId.ToString())
                );
            }

            await unitOfWork.SaveChangesAsync(ct);
            return (batch.Count, discarded);
        }
    }
}
