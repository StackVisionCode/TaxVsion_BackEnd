using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.FinancialInsights.Application.Imports;

namespace TaxVision.FinancialInsights.Infrastructure.Processing;

public sealed class TenantEntitlementsReconciliationOptions
{
    public const string SectionName = "FinancialInsights:EntitlementsReconciliation";

    /// <summary>Encendida por defecto: solo lee de Subscription y aplica revisiones iguales o más nuevas.</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalHours { get; set; } = 6;
    public int StartupDelaySeconds { get; set; } = 60;
    public int TenantLimitPerRun { get; set; } = 500;
}

/// <summary>
/// Repara la proyección del plan de los tenants conocidos (directorio u oficinas con lotes) a los que les falta la
/// fila o la tienen más vieja que el intervalo. Corre poco después del arranque y luego cada intervalo; un tenant
/// por scope y transacción. Sin lock distribuido: es idempotente (aplica la revisión igual o más nueva), así que
/// dos instancias a la vez solo repiten una lectura.
/// </summary>
public sealed class TenantEntitlementsReconciliationJob(
    IServiceProvider services,
    ILogger<TenantEntitlementsReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await services.GetRequiredService<IHostApplicationLifetime>().WaitForApplicationStartedAsync(stoppingToken);
        var options = services.GetRequiredService<IOptions<TenantEntitlementsReconciliationOptions>>().Value;
        if (!options.Enabled)
            return;

        var interval = TimeSpan.FromHours(Math.Max(1, options.IntervalHours));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, options.StartupDelaySeconds)), stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(DateTime.UtcNow - interval, Math.Max(1, options.TenantLimitPerRun), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "TenantEntitlementsReconciliationJob iteration failed.");
            }
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

    private async Task RunOnceAsync(DateTime staleBeforeUtc, int take, CancellationToken ct)
    {
        IReadOnlyList<Guid> tenants;
        using (var scope = services.CreateScope())
            tenants = await scope.ServiceProvider.GetRequiredService<ImportProcessingQueue>()
                .ListTenantsToRefreshAsync(staleBeforeUtc, take, ct);

        var (refreshed, failed) = (0, 0);
        foreach (var tenantId in tenants)
        {
            using var scope = services.CreateScope();
            var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
            using (correlation.Push(Guid.NewGuid().ToString("N")))
            {
                try
                {
                    if (await scope.ServiceProvider.GetRequiredService<TenantEntitlementsResolver>().RefreshAsync(tenantId, ct))
                        refreshed++;
                    else
                        failed++;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    failed++;
                    logger.LogWarning("Entitlements refresh failed for tenant {TenantId} with {ExceptionType}.", tenantId, ex.GetType().Name);
                }
            }
        }
        if (tenants.Count > 0)
            logger.LogInformation(
                "Entitlements reconciliation: {Refreshed} refreshed, {Failed} without snapshot of {Total} tenants.",
                refreshed,
                failed,
                tenants.Count
            );
    }
}
