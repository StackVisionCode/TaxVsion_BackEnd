using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.TenantTimeZones;

/// <summary>
/// Corre el reconciliador al arrancar (siembra la proyección de las oficinas existentes) y luego cada
/// <see cref="TenantTimeZoneReconciliationOptions.IntervalHours"/>; si una pasada no termina, reintenta en minutos.
/// Scope propio por pasada; un fallo no tumba el servicio.
/// </summary>
public sealed class TenantTimeZoneReconciliationJob(
    IServiceProvider serviceProvider,
    TimeProvider clock,
    ILogger<TenantTimeZoneReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForStartedAsync(serviceProvider.GetRequiredService<IHostApplicationLifetime>(), stoppingToken);

        var options = serviceProvider.GetRequiredService<IOptions<TenantTimeZoneReconciliationOptions>>().Value;
        if (!options.Enabled)
        {
            logger.LogInformation("TenantTimeZoneReconciliationJob disabled by config; not running.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.IntervalHours));
        while (!stoppingToken.IsCancellationRequested)
        {
            var completed = await RunOnceSafeAsync(Math.Clamp(options.PageSize, 1, 200), stoppingToken);
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

    /// <summary>true si la pasada terminó; false si Tenant no respondió o falló, para reintentar pronto.</summary>
    private async Task<bool> RunOnceSafeAsync(int pageSize, CancellationToken ct)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
            using (correlation.Push(Guid.NewGuid().ToString("N")))
            {
                var outcome = await TenantTimeZoneReconciler.RunAsync(
                    scope.ServiceProvider.GetRequiredService<ITenantDirectoryZoneClient>(),
                    scope.ServiceProvider.GetRequiredService<ITenantTimeZoneStore>(),
                    scope.ServiceProvider.GetRequiredService<ITenantTimeZoneResolver>(),
                    scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                    pageSize,
                    clock.GetUtcNow().UtcDateTime,
                    ct
                );

                if (!outcome.TenantReachable)
                    logger.LogWarning(
                        "Time zone reconciliation could not reach Tenant; applied {Count} before stopping.",
                        outcome.Applied
                    );
                else if (outcome.Applied > 0)
                    logger.LogInformation("Time zone reconciliation applied {Count} office zone(s).", outcome.Applied);
                return outcome.TenantReachable;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "TenantTimeZoneReconciliationJob iteration failed.");
            return false;
        }
    }

    private static async Task WaitForStartedAsync(IHostApplicationLifetime lifetime, CancellationToken ct)
    {
        var started = new TaskCompletionSource();
        await using var cancel = ct.Register(() => started.TrySetCanceled(ct));
        using var onStarted = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        try
        {
            await started.Task;
        }
        catch (OperationCanceledException) { }
    }
}
