using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Infrastructure.Jobs;

/// <summary>
/// Cada 24h recorre todos los tenants ya backfilleados (<see cref="ITenantBackfillStateRepository.ListAllTenantIdsAsync"/>)
/// y corrige drift de <c>CustomerEmailAddresses</c> vía <see cref="ICustomerEmailReconciliationService"/>
/// (plan §32 R1, "job de reconciliación diario"). Job puramente timer-tick, thin — la lógica real
/// de comparar-y-corregir vive en el servicio de Application (SRP, testeable sin
/// <c>BackgroundService</c>/DI de scope), mismo criterio que <c>WatchRenewalJob</c> de Connectors
/// separa el timer de <c>IWatchRenewalService</c>.
///
/// <para>
/// Timer-tick puro, sin evento de entrada que propagar — pero cada tenant reconciliado es una
/// llamada de red independiente a Customer.Api (paginada), así que se aísla con su propio scope y
/// una correlación NUEVA por tenant, exactamente como <c>WatchRenewalJob</c> aísla por
/// suscripción — un tenant lento/fallando no debe interferir con los logs/scope del resto.
/// </para>
/// </summary>
public sealed class CustomerEmailReconciliationJob(
    IServiceProvider serviceProvider,
    ILogger<CustomerEmailReconciliationJob> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

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
            logger.LogError(ex, "CustomerEmailReconciliationJob iteration failed.");
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var optionsScope = serviceProvider.CreateScope();
        var options = optionsScope
            .ServiceProvider.GetRequiredService<IOptions<CustomerEmailReconciliationOptions>>()
            .Value;
        if (!options.Enabled)
            return;

        var stateRepository = optionsScope.ServiceProvider.GetRequiredService<ITenantBackfillStateRepository>();
        var tenantIds = await stateRepository.ListAllTenantIdsAsync(ct);
        if (tenantIds.Count == 0)
            return;

        var tenantsWithDrift = 0;
        foreach (var tenantId in tenantIds)
        {
            if (await ReconcileTenantSafeAsync(tenantId, ct))
                tenantsWithDrift++;
        }

        if (tenantsWithDrift > 0)
            logger.LogInformation(
                "CustomerEmailReconciliationJob found drift in {Count}/{Total} tenants.",
                tenantsWithDrift,
                tenantIds.Count
            );
    }

    /// <summary>Devuelve <c>true</c> si esta corrida corrigió algo para el tenant — usado solo para el resumen final del run, ver <see cref="RunOnceAsync"/>.</summary>
    private async Task<bool> ReconcileTenantSafeAsync(Guid tenantId, CancellationToken ct)
    {
        using var tenantScope = serviceProvider.CreateScope();
        var reconciliationService =
            tenantScope.ServiceProvider.GetRequiredService<ICustomerEmailReconciliationService>();
        var correlation = tenantScope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        using (correlation.Push(Guid.NewGuid().ToString("N")))
        {
            var result = await reconciliationService.ReconcileTenantAsync(tenantId, ct);
            if (result.TotalFixed == 0)
                return false;

            logger.LogInformation(
                "CustomerEmailReconciliationJob fixed drift for tenant {TenantId}: {Created} created, {Updated} updated, {Reactivated} reactivated (completedFully={CompletedFully}).",
                tenantId,
                result.Created,
                result.Updated,
                result.Reactivated,
                result.CompletedFully
            );
            return true;
        }
    }
}
