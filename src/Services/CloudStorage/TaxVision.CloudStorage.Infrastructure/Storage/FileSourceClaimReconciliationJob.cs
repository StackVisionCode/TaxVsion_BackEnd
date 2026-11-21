using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

/// <summary>
/// Al arrancar y cada <see cref="FileSourceClaimOptions.IntervalHours"/>: pregunta a los servicios dueños por los
/// archivos disponibles sin origen declarado. Un mensaje por tenant y lote, cada uno en su unidad con outbox.
/// Lo declarado sale de la consulta, así que el trabajo se apaga solo. Un fallo nunca tumba el servicio.
/// </summary>
public sealed class FileSourceClaimReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    IOptions<FileSourceClaimOptions> options,
    ILogger<FileSourceClaimReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await lifetime.WaitForApplicationStartedAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!options.Value.Enabled)
        {
            logger.LogInformation("FileSourceClaimReconciliationJob disabled by config; not running.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.Value.IntervalHours));
        while (!stoppingToken.IsCancellationRequested)
        {
            var completed = await RunOnceSafeAsync(stoppingToken);
            try
            {
                await Task.Delay(ReconciliationRetry.NextDelay(completed, interval), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Una pasada. false si algo falló, para reintentar pronto.</summary>
    public async Task<bool> RunOnceSafeAsync(CancellationToken ct)
    {
        try
        {
            return await RunOnceAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FileSourceClaimReconciliationJob iteration failed.");
            return false;
        }
    }

    private async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        var batchSize = Math.Clamp(options.Value.BatchSize, 1, FileSourcesClaimRequestedIntegrationEvent.MaxFileIds);
        var correlationId = Guid.NewGuid().ToString("N");
        var batch = new List<Guid>(batchSize);
        var batchTenant = Guid.Empty;
        int ids = 0,
            batches = 0,
            failed = 0;

        async Task FlushAsync()
        {
            if (batch.Count == 0)
                return;
            ids += batch.Count;
            batches++;
            if (!await PublishAsync(batchTenant, [.. batch], correlationId, ct))
                failed++;
            batch.Clear();
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var files = scope.ServiceProvider.GetRequiredService<IFileObjectRepository>();
        UndeclaredSourceFile? after = null;
        while (true)
        {
            var page = await files.ListUndeclaredSourceFilesAsync(after, batchSize, ct);
            foreach (var file in page)
            {
                if (file.TenantId != batchTenant || batch.Count == batchSize)
                    await FlushAsync();
                batchTenant = file.TenantId;
                batch.Add(file.Id);
            }

            if (page.Count < batchSize)
                break;
            after = page[^1];
        }
        await FlushAsync();

        if (ids > 0)
            logger.LogInformation(
                "FileSourceClaimReconciliationJob requested owners for {Ids} file(s) in {Batches} batch(es), {Failed} failed.",
                ids,
                batches,
                failed
            );
        return failed == 0;
    }

    private async Task<bool> PublishAsync(Guid tenantId, Guid[] fileIds, string correlationId, CancellationToken ct)
    {
        var result = await TransactionalJobUnit.RunAsync<CloudStorageDbContext>(
            scopeFactory,
            async (unit, _) =>
            {
                await unit.PublishAsync(
                    new FileSourcesClaimRequestedIntegrationEvent
                    {
                        TenantId = tenantId,
                        FileIds = fileIds,
                        CorrelationId = correlationId,
                    }
                );
                return true;
            },
            logger,
            ct
        );
        return result.Outcome == JobUnitOutcome.Committed;
    }
}
