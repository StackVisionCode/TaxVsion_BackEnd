using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.Notification.Infrastructure.Retention;

internal sealed class NotificationRetentionJob(
    IServiceProvider serviceProvider,
    INotificationRetentionLock retentionLock,
    IOptions<NotificationRetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationRetentionJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("NotificationRetentionJob is disabled.");
            return;
        }

        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        await RunOnceAsync(settings, stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(settings.IntervalHours), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnceAsync(settings, stoppingToken);
    }

    private async Task RunOnceAsync(NotificationRetentionOptions settings, CancellationToken ct)
    {
        try
        {
            await using var handle = await retentionLock.TryAcquireAsync(
                TimeSpan.FromMinutes(settings.LockMinutes),
                ct
            );
            if (handle is null)
            {
                logger.LogDebug("NotificationRetentionJob skipped because another instance owns the lock.");
                return;
            }

            await using var scope = serviceProvider.CreateAsyncScope();
            var purger = scope.ServiceProvider.GetRequiredService<NotificationLogRetentionPurger>();
            var cutoffUtc = timeProvider.GetUtcNow().UtcDateTime.AddMonths(-settings.RetentionMonths);
            var purged = await purger.PurgeAsync(cutoffUtc, settings.BatchSize, ct);
            logger.LogInformation(
                "NotificationRetentionJob purged {PurgedCount} notification log(s) older than {CutoffUtc}.",
                purged,
                cutoffUtc
            );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "NotificationRetentionJob iteration failed.");
        }
    }
}
