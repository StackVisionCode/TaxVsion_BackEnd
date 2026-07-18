using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TaxVision.PaymentApp.Infrastructure.Scheduling;

/// <summary>
/// Base compartida por los jobs periódicos de PaymentApp: timer + lock distribuido (evita
/// que dos réplicas procesen el mismo batch) + scope de DI por iteración. Cada job concreto
/// solo implementa <see cref="RunOnceAsync"/> — mismo patrón que
/// <c>TaxVision.Subscription.Infrastructure.Scheduling.PeriodicSubscriptionJob</c>.
/// </summary>
public abstract class PeriodicPaymentAppJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger logger,
    TimeSpan interval,
    TimeSpan lockTtl
) : BackgroundService
{
    protected abstract string JobName { get; }

    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        do
        {
            await RunGuardedIterationAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunGuardedIterationAsync(CancellationToken ct)
    {
        try
        {
            await using var handle = await lockFactory.TryAcquireAsync($"job:{JobName}", lockTtl, ct);
            if (handle is null)
            {
                logger.LogDebug("{JobName} skipped this tick: another replica holds the lock.", JobName);
                return;
            }

            using var scope = scopeFactory.CreateScope();
            await RunOnceAsync(scope.ServiceProvider, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "{JobName} iteration failed.", JobName);
        }
    }
}
