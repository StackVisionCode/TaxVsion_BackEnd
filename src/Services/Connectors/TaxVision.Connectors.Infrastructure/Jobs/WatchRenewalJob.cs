using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Watch;

namespace TaxVision.Connectors.Infrastructure.Jobs;

/// <summary>
/// Cada hora busca suscripciones cuyo <c>ExpiresAtUtc</c> cae dentro de las próximas 24h (buffer
/// sobre expiry de 7d Gmail / ~2.9d Graph) y dispara su renewal vía <see cref="IWatchRenewalService"/>.
/// Mismo patrón que ProactiveTokenRefreshJob (Fase 4): scope propio por suscripción, correlation
/// fresca por iteración — este job es el consumer boundary, no WatchRenewalService.
/// </summary>
public sealed class WatchRenewalJob(IServiceProvider serviceProvider, ILogger<WatchRenewalJob> logger)
    : BackgroundService
{
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

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var subscriptionRepository = scope.ServiceProvider.GetRequiredService<IProviderWatchSubscriptionRepository>();

        var threshold = DateTime.UtcNow.Add(ExpiringWithin);
        var expiring = await subscriptionRepository.ListExpiringBeforeAsync(threshold, ct);
        if (expiring.Count == 0)
            return;

        var renewed = 0;
        foreach (var subscription in expiring)
        {
            using var subscriptionScope = serviceProvider.CreateScope();
            var renewalService = subscriptionScope.ServiceProvider.GetRequiredService<IWatchRenewalService>();
            var correlation = subscriptionScope.ServiceProvider.GetRequiredService<ICorrelationContext>();

            using (correlation.Push(Guid.NewGuid().ToString("N")))
            {
                var result = await renewalService.RenewAsync(subscription.Id, ct);
                if (result.IsSuccess)
                {
                    renewed++;
                }
                else
                {
                    logger.LogWarning(
                        "WatchRenewalJob could not renew subscription {SubscriptionId}: {Error}",
                        subscription.Id,
                        result.Error.Message
                    );
                }
            }
        }

        logger.LogInformation("WatchRenewalJob renewed {Renewed}/{Total} subscriptions.", renewed, expiring.Count);
    }
}
