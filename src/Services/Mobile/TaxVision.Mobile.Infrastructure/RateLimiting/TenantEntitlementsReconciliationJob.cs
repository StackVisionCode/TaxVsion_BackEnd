using BuildingBlocks.Common;
using BuildingBlocks.TenantTimeZones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Mobile.Application.RateLimiting;
using TaxVision.Mobile.Infrastructure.Persistence;

namespace TaxVision.Mobile.Infrastructure.RateLimiting;

public sealed class TenantEntitlementsReconciliationOptions
{
    public const string SectionName = "Mobile:EntitlementsReconciliation";

    /// <summary>Encendida por defecto: solo lee de Subscription y aplica revisiones iguales o más nuevas.</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalHours { get; set; } = 6;
    public int TenantLimitPerRun { get; set; } = 500;
}

/// <summary>Tenants cuyo plan hay que traer: los conocidos por Mobile sin fila o con una más vieja que el corte.</summary>
public sealed class TenantEntitlementsRefreshTargets(MobileDbContext db)
{
    public Task<bool> AnyKnownTenantAsync(CancellationToken ct) => KnownTenants().AnyAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListTenantsToRefreshAsync(
        DateTime staleBeforeUtc,
        int take,
        CancellationToken ct
    )
    {
        // Job entre oficinas: solo trae ids de oficina; cada reparación va después con la suya explícita.
        var plans = db.TenantPlanCodeProjections.IgnoreQueryFilters();
        return await KnownTenants()
            .Where(tenantId => !plans.Any(p => p.TenantId == tenantId && p.UpdatedAtUtc >= staleBeforeUtc))
            .OrderBy(tenantId => plans.Any(p => p.TenantId == tenantId))
            .Take(take)
            .ToListAsync(ct);
    }

    // Conocidos = directorio de Tenant (se reconcilia solo), personal proyectado o plan.
    // Sin filtro global: job entre oficinas que solo lista ids de oficina.
    private IQueryable<Guid> KnownTenants() =>
        db.Set<TenantTimeZoneProjection>()
            .Select(p => p.TenantId)
            .Union(db.UserPermissionsProjections.IgnoreQueryFilters().Select(p => p.TenantId))
            .Union(db.TenantPlanCodeProjections.IgnoreQueryFilters().Select(p => p.TenantId));
}

/// <summary>
/// Repara la proyección del plan (Subscription no repara a los consumidores): corre al arrancar, sembrando a los
/// tenants anteriores al servicio, y luego cada intervalo; si una pasada no termina, reintenta en minutos.
/// Un tenant por scope; idempotente (aplica la revisión igual o más nueva), así que no necesita candado.
/// </summary>
public sealed class TenantEntitlementsReconciliationJob(
    IServiceProvider services,
    TimeProvider clock,
    ILogger<TenantEntitlementsReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForStartedAsync(services.GetRequiredService<IHostApplicationLifetime>(), stoppingToken);

        var options = services.GetRequiredService<IOptions<TenantEntitlementsReconciliationOptions>>().Value;
        if (!options.Enabled)
        {
            logger.LogInformation("TenantEntitlementsReconciliationJob disabled by config; not running.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.IntervalHours));
        while (!stoppingToken.IsCancellationRequested)
        {
            var completed = await RunOnceSafeAsync(
                clock.GetUtcNow().UtcDateTime - interval,
                Math.Max(1, options.TenantLimitPerRun),
                stoppingToken
            );
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

    /// <summary>true si la pasada terminó; false si Subscription no respondió para algún tenant o falló.</summary>
    private async Task<bool> RunOnceSafeAsync(DateTime staleBeforeUtc, int take, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<Guid> tenants;
            using (var scope = services.CreateScope())
            {
                var targets = scope.ServiceProvider.GetRequiredService<TenantEntitlementsRefreshTargets>();
                // Sin oficinas conocidas no hay nada que reparar: el directorio aún no se llenó. Reintenta pronto.
                if (!await targets.AnyKnownTenantAsync(ct))
                {
                    logger.LogWarning(
                        "Entitlements reconciliation: no known tenants yet (tenant directory empty); retrying."
                    );
                    return false;
                }
                tenants = await targets.ListTenantsToRefreshAsync(staleBeforeUtc, take, ct);
            }

            var (refreshed, failed) = (0, 0);
            foreach (var tenantId in tenants)
            {
                using var scope = services.CreateScope();
                var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
                using (correlation.Push(Guid.NewGuid().ToString("N")))
                {
                    if (
                        await scope
                            .ServiceProvider.GetRequiredService<TenantEntitlementsResolver>()
                            .RefreshAsync(tenantId, ct)
                    )
                        refreshed++;
                    else
                        failed++;
                }
            }

            if (failed > 0)
                logger.LogWarning(
                    "Entitlements reconciliation: {Refreshed} refreshed, {Failed} without snapshot of {Total} tenants.",
                    refreshed,
                    failed,
                    tenants.Count
                );
            else
                logger.LogInformation(
                    "Entitlements reconciliation: {Refreshed} of {Total} target tenant(s) refreshed.",
                    refreshed,
                    tenants.Count
                );
            return failed == 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "TenantEntitlementsReconciliationJob iteration failed.");
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
