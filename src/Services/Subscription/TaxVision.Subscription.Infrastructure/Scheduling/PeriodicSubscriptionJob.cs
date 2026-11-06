using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Base compartida por los jobs de renovación/expiración/grace: timer + lock distribuido
/// (evita que dos réplicas procesen el mismo batch) + scope de DI por iteración. Cada job
/// concreto solo implementa <see cref="RunOnceAsync"/> con su query y su transición de
/// dominio — la orquestación (cuándo correr, cómo evitar solaparse, cómo loguear fallos)
/// vive una sola vez aquí.
/// </summary>
public abstract class PeriodicSubscriptionJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger logger,
    TimeSpan interval,
    TimeSpan lockTtl
) : BackgroundService
{
    protected abstract string JobName { get; }

    // Para abrir un scope por ítem dentro de la pasada.
    protected IServiceScopeFactory ScopeFactory => scopeFactory;

    protected ILogger Logger => logger;

    /// <summary>
    /// BB-3: una unidad transaccional por ítem. La fila, sus eventos de dominio y lo publicado por
    /// <see cref="JobUnitContext{TDbContext}.PublishAsync"/> se confirman juntos; un fallo o un conflicto no
    /// arrastra a los demás ítems.
    /// </summary>
    protected Task<JobBatchSummary> RunUnitsAsync<TItem>(
        IEnumerable<TItem> items,
        Func<TItem, JobUnitContext<SubscriptionDbContext>, CancellationToken, Task<bool>> work,
        CancellationToken ct
    ) => TransactionalJobUnit.RunEachAsync(scopeFactory, items, work, logger, ct);

    /// <summary>Una sola unidad transaccional (BB-3), para los jobs que preparan cada ítem fuera de la transacción.</summary>
    protected Task<JobUnitResult> RunUnitAsync(
        Func<JobUnitContext<SubscriptionDbContext>, CancellationToken, Task<bool>> work,
        CancellationToken ct
    ) => TransactionalJobUnit.RunAsync(scopeFactory, work, logger, ct);

    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Primer tick inmediato — sin esto, un job que resuelve IMessageBus dentro de
        // RunOnceAsync puede ganarle la carrera al arranque de Wolverine y reventar con
        // WolverineHasNotStartedException. IHostApplicationLifetime es singleton: resolverlo
        // desde un scope hijo (en vez de inyectarlo en cada uno de los ~9 jobs concretos) es
        // equivalente y no obliga a tocar sus constructores.
        using (var scope = scopeFactory.CreateScope())
        {
            var lifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();
            await lifetime.WaitForApplicationStartedAsync(stoppingToken);
        }

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
