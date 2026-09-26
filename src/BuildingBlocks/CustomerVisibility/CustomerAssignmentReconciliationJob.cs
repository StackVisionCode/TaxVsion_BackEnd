using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.CustomerVisibility;

/// <summary>
/// Siembra / auto-repara la proyección compartida de asignaciones: re-pagina la fuente autoritativa completa
/// (todos los tenants) y hace upsert version-guarded. Idempotente (misma comparación por Version que el
/// consumer). Espera el arranque del host; scope propio por corrida; un fallo no tumba el servicio.
/// </summary>
public sealed class CustomerAssignmentReconciliationJob(
    IServiceProvider serviceProvider,
    ILogger<CustomerAssignmentReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await WaitForStartedAsync(lifetime, stoppingToken);

        var options = serviceProvider.GetRequiredService<IOptions<CustomerVisibilityReconciliationOptions>>().Value;
        if (!options.ReconciliationEnabled)
        {
            logger.LogInformation("CustomerAssignmentReconciliationJob disabled by config; not running.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.ReconciliationIntervalHours));
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceSafeAsync(options.ReconciliationPageSize, stoppingToken);
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private static async Task WaitForStartedAsync(IHostApplicationLifetime lifetime, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource();
        await using var ctReg = ct.Register(() => tcs.TrySetCanceled(ct));
        using var startedReg = lifetime.ApplicationStarted.Register(() => tcs.TrySetResult());
        try
        {
            await tcs.Task;
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunOnceSafeAsync(int pageSize, CancellationToken ct)
    {
        try
        {
            await RunOnceAsync(pageSize, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CustomerAssignmentReconciliationJob iteration failed.");
        }
    }

    private async Task RunOnceAsync(int pageSize, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ICustomerAssignmentsReconciliationClient>();
        var store = scope.ServiceProvider.GetRequiredService<ICustomerAssignmentProjectionStore>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        using (correlation.Push(Guid.NewGuid().ToString("N")))
        {
            var page = 1;
            var applied = 0;

            while (true)
            {
                var result = await client.ListPageAsync(page, pageSize, ct);
                if (result is null)
                {
                    logger.LogWarning(
                        "CustomerAssignmentReconciliationJob aborted on page {Page} (Customer.Api unreachable/unauthorized).",
                        page
                    );
                    return;
                }

                foreach (var item in result.Items)
                {
                    var current = await store.GetVersionAsync(item.TenantId, item.CustomerId, ct);
                    if (current is { } v && item.Version <= v)
                        continue;
                    await store.ReplaceAsync(item.TenantId, item.CustomerId, item.AssigneeUserIds, item.Version, ct);
                    applied++;
                }

                await unitOfWork.SaveChangesAsync(ct);

                if (!result.HasMore)
                    break;
                page++;
            }

            if (applied > 0)
                logger.LogInformation(
                    "CustomerAssignmentReconciliationJob applied {Count} customer assignment snapshot(s).",
                    applied
                );
        }
    }
}
