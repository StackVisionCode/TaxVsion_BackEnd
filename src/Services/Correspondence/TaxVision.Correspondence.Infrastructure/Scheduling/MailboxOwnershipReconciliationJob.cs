using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Projections;

namespace TaxVision.Correspondence.Infrastructure.Scheduling;

/// <summary>
/// Llena y repara la proyección de dueños de buzones. Corre al arrancar: hasta que un tenant tiene filas, la visibilidad
/// le pregunta a Connectors. Un scope por tenant: uno que falla no corta a los demás.
/// </summary>
public sealed class MailboxOwnershipReconciliationJob(
    IServiceProvider serviceProvider,
    IOptions<MailboxOwnershipReconciliationOptions> options,
    ILogger<MailboxOwnershipReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        if (!options.Value.Enabled)
        {
            logger.LogInformation("MailboxOwnershipReconciliationJob disabled by config; not running.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.Value.IntervalHours));
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceSafeAsync(stoppingToken);
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

    private async Task RunOnceSafeAsync(CancellationToken ct)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var tenantIds = await scope
                .ServiceProvider.GetRequiredService<ITenantBackfillStateRepository>()
                .ListAllTenantIdsAsync(ct);

            foreach (var tenantId in tenantIds)
                await ReconcileTenantSafeAsync(tenantId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "MailboxOwnershipReconciliationJob iteration failed.");
        }
    }

    private async Task ReconcileTenantSafeAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
            using (correlation.Push(Guid.NewGuid().ToString("N")))
                await scope
                    .ServiceProvider.GetRequiredService<MailboxOwnershipReconciliation>()
                    .ReconcileTenantAsync(tenantId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not reconcile mailbox owners of tenant {TenantId}.", tenantId);
        }
    }
}
