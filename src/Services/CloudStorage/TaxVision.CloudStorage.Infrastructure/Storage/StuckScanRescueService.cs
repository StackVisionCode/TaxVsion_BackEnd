using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Commands;
using Wolverine;
using Wolverine.Runtime;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

/// <summary>
/// CST-1: cada <see cref="CloudStorageOptions.StuckScanRescueIntervalMinutes"/> busca archivos que siguen en
/// PendingScan/Scanning después del umbral y despacha <see cref="RescueStuckScanCommand"/> por cada uno. Cada
/// invocación corre en el pipeline de Wolverine con su transacción y outbox. Candado para una sola réplica.
/// </summary>
public sealed class StuckScanRescueService(
    IServiceScopeFactory scopeFactory,
    IWolverineRuntime runtime,
    IHostApplicationLifetime lifetime,
    IOptions<CloudStorageOptions> options,
    ILogger<StuckScanRescueService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.StuckScanRescueEnabled)
            return;

        // Sin esperar el arranque, invocar puede ganarle a Wolverine (WolverineHasNotStartedException).
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, settings.StuckScanRescueIntervalMinutes)));
        do
        {
            try
            {
                await RunOnceAsync(settings, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Stuck scan rescue iteration failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CloudStorageOptions settings, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await using var handle = await scope
            .ServiceProvider.GetRequiredService<SqlStuckScanRescueLock>()
            .TryAcquireAsync(ct);
        if (handle is null)
            return;

        var clock = scope.ServiceProvider.GetRequiredService<ISystemClock>();
        var olderThan = clock.UtcNow.AddMinutes(-settings.StuckScanThresholdMinutes);
        var stuck = await scope
            .ServiceProvider.GetRequiredService<IFileObjectRepository>()
            .ListStuckScansAsync(olderThan, settings.StuckScanRescueBatchSize, ct);
        if (stuck.Count == 0)
            return;

        // Bus propio (no del scope): el handler publica dentro de su transacción.
        var bus = new MessageBus(runtime);
        var failed = 0;
        foreach (var file in stuck)
        {
            try
            {
                await bus.InvokeAsync(new RescueStuckScanCommand(file.TenantId, file.Id), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogError(ex, "Stuck scan rescue failed for file {FileId}.", file.Id);
            }
        }

        logger.LogWarning(
            "Stuck scan rescue processed {Count} file(s), {Failed} failed.",
            stuck.Count - failed,
            failed
        );
    }
}
