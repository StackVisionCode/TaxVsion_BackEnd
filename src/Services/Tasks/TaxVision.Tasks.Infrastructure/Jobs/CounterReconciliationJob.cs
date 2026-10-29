using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Results;
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
    private static readonly TimeSpan ConflictBackoff = TimeSpan.FromMilliseconds(500);
    private const int BatchSize = 200;
    private const int MaxAttempts = 3;

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

    // Al arrancar corre a la vez que los otros jobs de Tasks: si alguno toca la misma tarea entre la lectura y
    // el guardado, el RowVersion choca. Se relee en un scope nuevo y se reintenta; no es un fallo real.
    public async Task RunOnceSafeAsync(CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await RunOnceAsync(ct);
                return;
            }
            catch (ConflictException) when (attempt < MaxAttempts)
            {
                await Task.Delay(ConflictBackoff, ct);
            }
            catch (ConflictException ex)
            {
                logger.LogWarning(
                    ex,
                    "CounterReconciliationJob kept conflicting with concurrent task updates; the next pass retries."
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "CounterReconciliationJob iteration failed.");
                return;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        using (correlation.Push(Guid.NewGuid().ToString("N")))
        {
            var reconciler = scope.ServiceProvider.GetRequiredService<ICounterReconciler>();
            var fixedCount = await reconciler.ReconcileAsync(BatchSize, ct);
            scope.ServiceProvider.GetRequiredService<ITaskMetrics>().RecordReconciliationCorrections(fixedCount);
            if (fixedCount > 0)
                logger.LogInformation("CounterReconciliationJob corrected {Count} task counter(s).", fixedCount);
        }
    }
}
