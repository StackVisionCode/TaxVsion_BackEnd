using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Infrastructure.Scheduling;

namespace TaxVision.Tenant.Infrastructure.Team;

/// <summary>
/// Purga diaria del historial de actividad (más de 13 meses). Un lote por unidad: cada DELETE es su propia
/// transacción y un fallo deja el resto para la próxima pasada. El lock de Redis deja una sola réplica a la vez.
/// </summary>
internal sealed class ActivityRetentionJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory locks,
    IOptions<ActivityRetentionOptions> options,
    ILogger<ActivityRetentionJob> logger
) : BackgroundService
{
    private const string LockKey = "job:activity-retention";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = scopeFactory.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<IHostApplicationLifetime>()
                .WaitForApplicationStartedAsync(stoppingToken);

        var settings = options.Value;
        if (!settings.Enabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromHours(settings.IntervalHours));
        do
        {
            try
            {
                await RunGuardedAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ActivityRetentionJob failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunGuardedAsync(ActivityRetentionOptions settings, CancellationToken ct)
    {
        await using var handle = await locks.TryAcquireAsync(
            LockKey,
            TimeSpan.FromMinutes(settings.LockTtlMinutes),
            ct
        );
        if (handle is null)
        {
            logger.LogDebug("ActivityRetentionJob skipped this tick: another replica holds the lock.");
            return;
        }

        var deleted = await SweepAsync(DateTime.UtcNow, ct);
        if (deleted > 0)
            logger.LogInformation("ActivityRetentionJob purged {Deleted} activity entries.", deleted);
    }

    /// <summary>Una pasada completa; devuelve cuántas entradas borró.</summary>
    internal async Task<int> SweepAsync(DateTime nowUtc, CancellationToken ct)
    {
        var cutoff = ActivityRetention.CutoffFor(nowUtc);
        var batchSize = options.Value.BatchSize;
        var total = 0;
        while (!ct.IsCancellationRequested)
        {
            int deleted;
            await using (var scope = scopeFactory.CreateAsyncScope())
                deleted = await scope
                    .ServiceProvider.GetRequiredService<ActivityRetentionPurger>()
                    .PurgeBatchAsync(cutoff, batchSize, ct);

            total += deleted;
            if (deleted < batchSize)
                break;
        }

        return total;
    }
}
