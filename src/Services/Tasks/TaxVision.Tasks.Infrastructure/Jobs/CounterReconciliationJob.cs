using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Counters.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Infrastructure.Jobs;

/// <summary>Cruza tenants a propósito: no hay actor autenticado, así que las lecturas van con
/// <c>IgnoreQueryFilters()</c>.</summary>
public sealed class CounterReconciliationJob(IServiceProvider serviceProvider, ILogger<CounterReconciliationJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private const int BatchSize = 200;

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
        using var scope = serviceProvider.CreateScope();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        using (correlation.Push(Guid.NewGuid().ToString("N")))
        {
            try
            {
                var reconciler = scope.ServiceProvider.GetRequiredService<ICounterReconciler>();
                var fixedCount = await reconciler.ReconcileAsync(BatchSize, ct);
                scope.ServiceProvider.GetRequiredService<ITaskMetrics>().RecordReconciliationCorrections(fixedCount);
                if (fixedCount > 0)
                    logger.LogInformation("CounterReconciliationJob corrected {Count} task counter(s).", fixedCount);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CounterReconciliationJob iteration failed.");
            }
        }
    }
}
