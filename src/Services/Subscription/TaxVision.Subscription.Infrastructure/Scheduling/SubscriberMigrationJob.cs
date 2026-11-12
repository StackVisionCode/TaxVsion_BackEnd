using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Migrations;
using TaxVision.Subscription.Domain.Migrations;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// D11: ejecuta las migraciones de oficinas, UNA oficina por transacción con outbox. Antes de abrir la transacción
/// mide si la oficina entra (HTTP a Auth y CloudStorage, solo si la edición nueva recorta). Una migración cancelada
/// desagenda lo que todavía no se aplicó. Repetir la pasada no duplica nada: cada oficina se marca al procesarla.
/// </summary>
public sealed class SubscriberMigrationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<SubscriberMigrationJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10))
{
    protected override string JobName => "subscriber-migration";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var migrations = await services.GetRequiredService<ISubscriberMigrationRepository>().ListWithWorkAsync(ct);
        foreach (var migration in migrations)
        {
            foreach (var tenantId in migration.PendingTenantIds())
                await ProcessOfficeAsync(migration, tenantId, cancel: false, ct);
            foreach (var tenantId in migration.ScheduledTenantIds())
                await ProcessOfficeAsync(migration, tenantId, cancel: true, ct);
        }
    }

    internal async Task ProcessOfficeAsync(
        SubscriberMigration snapshot,
        Guid tenantId,
        bool cancel,
        CancellationToken ct
    )
    {
        // Medición fuera de la transacción (L): nunca HTTP con una transacción abierta.
        var capacityBlock = cancel ? null : await MeasureAsync(snapshot, tenantId, ct);

        await RunUnitAsync(
            async (unit, token) =>
            {
                unit.UseTenant(tenantId);
                var migration = await unit
                    .Services.GetRequiredService<ISubscriberMigrationRepository>()
                    .GetForUpdateAsync(snapshot.Id, token);
                var office = await unit
                    .Services.GetRequiredService<ISubscriptionRepository>()
                    .GetByTenantIdAsync(tenantId, token);
                if (migration is null)
                    return false;
                var nowUtc = DateTime.UtcNow;

                if (office is null)
                {
                    migration.MarkSkipped(tenantId, "Subscription.NotFound", nowUtc);
                    return true;
                }

                if (cancel)
                {
                    SubscriberMigrationSteps.UnscheduleOffice(migration, office, nowUtc);
                    return true;
                }

                var plan = await unit
                    .Services.GetRequiredService<IPlanRepository>()
                    .GetByIdAsync(migration.PlanId, token);
                if (plan is null)
                {
                    migration.MarkBlocked(tenantId, "Plan.NotFound", nowUtc);
                    return true;
                }

                var catalog = await unit.Services.GetRequiredService<IModuleRepository>().GetAllAsync(token);
                SubscriberMigrationSteps.ApplyToOffice(migration, office, plan, catalog, capacityBlock, nowUtc);
                return true;
            },
            ct
        );
    }

    private async Task<string?> MeasureAsync(SubscriberMigration migration, Guid tenantId, CancellationToken ct)
    {
        // Mover ya solo se permite si es pura mejora: no recorta nada, no hace falta medir.
        if (migration.Mode == SubscriberMigrationMode.Immediate)
            return null;

        await using var scope = ScopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BuildingBlocks.Tenancy.ITenantContext>().SetTenant(tenantId);
        var office = await services.GetRequiredService<ISubscriptionRepository>().GetByTenantIdAsync(tenantId, ct);
        var plan = await services.GetRequiredService<IPlanRepository>().GetByIdAsync(migration.PlanId, ct);
        if (office is null || plan is null || office.PlanId != migration.PlanId)
            return null;

        var catalog = await services.GetRequiredService<IModuleRepository>().GetAllAsync(ct);
        return await services
            .GetRequiredService<SubscriberMigrationPlanner>()
            .CapacityBlockAsync(office, plan, migration.ToVersionId, catalog, ct);
    }
}
