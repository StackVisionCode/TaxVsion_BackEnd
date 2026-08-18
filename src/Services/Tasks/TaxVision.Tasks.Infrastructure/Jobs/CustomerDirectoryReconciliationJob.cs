using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Customers.Abstractions;
using TaxVision.Tasks.Application.Projections.Abstractions;

namespace TaxVision.Tasks.Infrastructure.Jobs;

/// <summary>
/// Cada 6 h recorre los tenants con nombres faltantes en el directorio y los rellena repaginando
/// Customer. Scope propio y correlación fresca por tenant: uno lento o que falla no frena al resto.
///
/// <para>
/// Sólo completa nombres de filas que ya existen. Insertar customers que Task nunca vio es trabajo
/// de <see cref="TenantCustomerFullReconciliationJob"/>.
/// </para>
/// </summary>
public sealed class CustomerDirectoryReconciliationJob(
    IServiceProvider serviceProvider,
    ILogger<CustomerDirectoryReconciliationJob> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const int PageSize = 100;

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
            logger.LogError(ex, "CustomerDirectoryReconciliationJob iteration failed.");
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var optionsScope = serviceProvider.CreateScope();
        var options = optionsScope
            .ServiceProvider.GetRequiredService<IOptions<CustomerDirectoryReconciliationOptions>>()
            .Value;
        if (!options.Enabled)
            return;

        var directoryRepository = optionsScope.ServiceProvider.GetRequiredService<ICustomerDirectoryRepository>();
        var tenantIds = await directoryRepository.ListTenantIdsWithMissingNamesAsync(options.TenantLimitPerRun, ct);
        if (tenantIds.Count == 0)
            return;

        var tenantsFixed = 0;
        foreach (var tenantId in tenantIds)
        {
            if (await ReconcileTenantSafeAsync(tenantId, ct))
                tenantsFixed++;
        }

        if (tenantsFixed > 0)
            logger.LogInformation(
                "CustomerDirectoryReconciliationJob filled missing names for {Count}/{Total} tenants.",
                tenantsFixed,
                tenantIds.Count
            );
    }

    /// <summary>Devuelve <c>true</c> si esta corrida rellenó al menos un nombre para el tenant.</summary>
    private async Task<bool> ReconcileTenantSafeAsync(Guid tenantId, CancellationToken ct)
    {
        using var tenantScope = serviceProvider.CreateScope();
        var directoryRepository = tenantScope.ServiceProvider.GetRequiredService<ICustomerDirectoryRepository>();
        var customerClient = tenantScope.ServiceProvider.GetRequiredService<ITasksCustomerClient>();
        var unitOfWork = tenantScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var correlation = tenantScope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        using (correlation.Push(Guid.NewGuid().ToString("N")))
        {
            try
            {
                var page = 1;
                while (true)
                {
                    var result = await customerClient.ListActiveCustomersAsync(tenantId, page, PageSize, ct);
                    if (result is null)
                    {
                        logger.LogWarning(
                            "CustomerDirectoryReconciliationJob aborted for tenant {TenantId} — Customer listing call failed on page {Page}.",
                            tenantId,
                            page
                        );
                        break;
                    }

                    foreach (var customer in result.Items)
                        await directoryRepository.ApplyDisplayNameIfMissingAsync(
                            tenantId,
                            customer.Id,
                            customer.DisplayName,
                            ct
                        );

                    if (!result.HasMore)
                        break;
                    page++;
                }

                var rowsChanged = await unitOfWork.SaveChangesAsync(ct);
                if (rowsChanged == 0)
                    return false;

                logger.LogInformation(
                    "CustomerDirectoryReconciliationJob filled {Count} display name(s) for tenant {TenantId}.",
                    rowsChanged,
                    tenantId
                );
                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "CustomerDirectoryReconciliationJob failed for tenant {TenantId}.", tenantId);
                return false;
            }
        }
    }
}
