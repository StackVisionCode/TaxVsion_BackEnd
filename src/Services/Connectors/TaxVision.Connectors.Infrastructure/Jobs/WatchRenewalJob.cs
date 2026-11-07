using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Watch;

namespace TaxVision.Connectors.Infrastructure.Jobs;

/// <summary>
/// Cada hora busca suscripciones cuyo <c>ExpiresAtUtc</c> cae dentro de las próximas 24h (buffer
/// sobre expiry de 7d Gmail / ~2.9d Graph) y dispara su renewal vía <see cref="IWatchRenewalService"/>.
/// Mismo patrón que ProactiveTokenRefreshJob (Fase 4): scope propio por suscripción, correlation
/// fresca por iteración — este job es el consumer boundary, no WatchRenewalService.
/// </summary>
public sealed class WatchRenewalJob(
    IServiceProvider serviceProvider,
    IOptions<WatchRenewalOptions> options,
    ILogger<WatchRenewalJob> logger
) : BackgroundService
{
    private const string LeaderLockKey = "connectors:watch-renewal:leader";

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan ExpiringWithin = TimeSpan.FromHours(24);

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
            logger.LogError(ex, "WatchRenewalJob iteration failed.");
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        // Con varias réplicas, solo una recorre las suscripciones en cada vuelta.
        var leaderLockTtl = TimeSpan.FromMinutes(Math.Max(1, options.Value.LeaderLockTtlMinutes));
        var distributedLock = serviceProvider.GetRequiredService<IDistributedLock>();
        await using var leader = await distributedLock.AcquireAsync(LeaderLockKey, leaderLockTtl, ct);
        if (!leader.IsAcquired)
        {
            logger.LogDebug("WatchRenewalJob skipped — another instance holds the leader lock.");
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var subscriptionRepository = scope.ServiceProvider.GetRequiredService<IProviderWatchSubscriptionRepository>();

        var threshold = DateTime.UtcNow.Add(ExpiringWithin);
        var expiring = await subscriptionRepository.ListExpiringBeforeAsync(threshold, ct);
        if (expiring.Count == 0)
            return;

        var renewed = 0;
        foreach (var subscription in expiring)
        {
            if (!await leader.ExtendAsync(leaderLockTtl, ct))
            {
                logger.LogWarning("WatchRenewalJob lost the leader lock — stopping this pass.");
                return;
            }

            if (await RenewOneAsync(subscription.Id, ct))
                renewed++;
        }

        logger.LogInformation("WatchRenewalJob renewed {Renewed}/{Total} subscriptions.", renewed, expiring.Count);
    }

    // El proveedor se llama sin transacción; la falla se confirma en su propia unidad (IMailboxFailureUnit).
    // Un error en una suscripción no corta el barrido.
    private async Task<bool> RenewOneAsync(Guid subscriptionId, CancellationToken ct)
    {
        using var subscriptionScope = serviceProvider.CreateScope();
        var renewalService = subscriptionScope.ServiceProvider.GetRequiredService<IWatchRenewalService>();
        var correlation = subscriptionScope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        using (correlation.Push(Guid.NewGuid().ToString("N")))
        {
            try
            {
                var result = await renewalService.RenewAsync(subscriptionId, ct);
                if (result.IsSuccess)
                    return true;

                logger.LogWarning(
                    "WatchRenewalJob could not renew subscription {SubscriptionId}: {Error}",
                    subscriptionId,
                    result.Error.Message
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "WatchRenewalJob failed on subscription {SubscriptionId}.", subscriptionId);
            }
            return false;
        }
    }
}
