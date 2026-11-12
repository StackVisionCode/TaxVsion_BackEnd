using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForPlan;
using TaxVision.Subscription.Application.Plans.Commands.SetPlanModules;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Configuration;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Scheduling;
using Wolverine;

namespace TaxVision.Subscription.Api.Bootstrap;

public sealed class SubscriptionCatalogReconciler(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<SubscriptionCatalogReconciler> logger,
    IOptions<SubscriptionCatalogOptions> catalogOptions,
    IDistributedLockFactory lockFactory
) : DeferredStartupHostedService(lifetime, logger)
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var lease = await lockFactory.TryAcquireAsync(
            "bootstrap:catalog-reconcile",
            catalogOptions.Value.ReconcileLockTtl,
            cancellationToken
        );
        if (lease is null)
        {
            logger.LogInformation("Catalog reconcile skipped: another replica holds the lock.");
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var log = services.GetRequiredService<ILogger<SubscriptionCatalogReconciler>>();
        var db = services.GetRequiredService<SubscriptionDbContext>();

        await SubscriptionModuleCatalogSeeder.SeedAsync(db, catalogOptions.Value, log, cancellationToken);
        await ReconcilePlanModulesAsync(services, log, cancellationToken);
        await ReconcileCatalogEntitlementsAsync(services, log, cancellationToken);
        await ReconcileQuotaAddOnsAsync(services, log, cancellationToken);
        await services.GetRequiredService<ICommercialModuleShadowReconciler>().ReconcileAllAsync(cancellationToken);
    }

    private static async Task ReconcilePlanModulesAsync(IServiceProvider services, ILogger log, CancellationToken ct)
    {
        var plans = services.GetRequiredService<IPlanRepository>();
        var modules = await services.GetRequiredService<IModuleRepository>().GetAllAsync(ct);
        var bus = services.GetRequiredService<IMessageBus>();
        var commercialCodes = modules
            .Where(module => module.Category == ModuleCategory.Commercial)
            .Select(module => module.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var plan in await plans.GetAllAsync(ct))
        {
            var current = PublishedModulesOf(plan);
            if (current is null)
                continue;

            var selectedCommercial = current.Where(commercialCodes.Contains).Order(StringComparer.Ordinal).ToArray();
            var desired = modules
                .Where(module => module.Category == ModuleCategory.Core || selectedCommercial.Contains(module.Code))
                .Select(module => module.Code)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!NeedsRevision(current, desired))
                continue;

            var result = await bus.InvokeAsync<BuildingBlocks.Results.Result>(
                new SetPlanModulesCommand(plan.Id, selectedCommercial, Guid.Empty, SystemActor: ReconcilerActorName),
                ct
            );
            if (result.IsFailure)
            {
                log.LogError(
                    "Catalog reconciler could not revise plan {PlanCode}: {ErrorCode}.",
                    plan.Code.Value,
                    result.Error.Code
                );
                continue;
            }

            log.LogInformation("Catalog reconciler added implicit core modules to plan {PlanCode}.", plan.Code.Value);
        }
    }

    public static HashSet<string>? PublishedModulesOf(SubscriptionPlan plan)
    {
        var published = plan.Versions.FirstOrDefault(version => version.Status == PlanVersionStatus.Published);
        if (published is null)
            return null;

        return PlanVersionEntitlements.GetEnabledModules(published).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool NeedsRevision(IReadOnlySet<string> current, IEnumerable<string> desired) =>
        !current.SetEquals(desired);

    // D8: todo plan no archivado recibe las claves de plataforma; el código del plan no decide nada.
    public static bool ReceivesCatalogEntitlements(SubscriptionPlan plan) => plan.Status != PlanStatus.Archived;

    // Idempotente: solo versiona el plan cuya versión publicada no trae todas las claves de catálogo
    // (communication.*, storage.* de archivos, signature.*), en UNA versión nueva con todas las que falten; el
    // recálculo por plan propaga la versión nueva a cada tenant vía entitlements_changed.
    private static async Task ReconcileCatalogEntitlementsAsync(
        IServiceProvider services,
        ILogger log,
        CancellationToken ct
    )
    {
        var plans = services.GetRequiredService<IPlanRepository>();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();

        foreach (var snapshot in await plans.GetAllAsync(ct))
        {
            if (!ReceivesCatalogEntitlements(snapshot))
                continue;

            var published = snapshot.GetPublishedVersion();
            if (published is null)
            {
                log.LogWarning(
                    "Catalog reconciler skipped catalog entitlements for plan {PlanCode}: no published version.",
                    snapshot.Code.Value
                );
                continue;
            }

            if (EntitlementKeyCatalog.MissingFrom(published).Count == 0)
                continue;

            // BB-3: la versión nueva del plan y su recálculo por plan se confirman juntos.
            await TransactionalJobUnit.RunAsync<SubscriptionDbContext>(
                scopeFactory,
                async (unit, token) =>
                {
                    var plan = await unit
                        .Services.GetRequiredService<IPlanRepository>()
                        .GetByIdForUpdateAsync(snapshot.Id, token);
                    if (plan is null)
                        return false;

                    var before = CatalogAuditViews.Of(plan);
                    var result = plan.AddMissingEntitlements(
                        EntitlementKeyCatalog.PlatformDefaults,
                        Guid.Empty,
                        DateTime.UtcNow
                    );
                    if (result.IsFailure)
                    {
                        log.LogError(
                            "Catalog reconciler could not add catalog entitlements to plan {PlanCode}: {ErrorCode}.",
                            plan.Code.Value,
                            result.Error.Code
                        );
                        return false;
                    }

                    if (result.Value == 0)
                        return false;

                    await CatalogAudit.RecordAsync(
                        unit.Services.GetRequiredService<ICatalogAuditLog>(),
                        Reconciler,
                        "plan.entitlements_added",
                        CatalogAuditViews.Target(plan),
                        before,
                        new { Added = result.Value, Plan = CatalogAuditViews.Of(plan) },
                        reason: null,
                        Reconciler.Label,
                        DateTime.UtcNow,
                        token
                    );

                    await unit.PublishAsync(new RecalculateEntitlementsForPlanCommand(plan.Id));
                    log.LogInformation(
                        "Catalog reconciler added {Count} catalog entitlements to plan {PlanCode}.",
                        result.Value,
                        plan.Code.Value
                    );
                    return true;
                },
                log,
                ct
            );
        }
    }

    private static async Task ReconcileQuotaAddOnsAsync(IServiceProvider services, ILogger log, CancellationToken ct)
    {
        var db = services.GetRequiredService<SubscriptionDbContext>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var audit = services.GetRequiredService<ICatalogAuditLog>();
        var changed = new List<string>();

        foreach (var quota in QuotaAddOnCatalog.All.Where(addOn => addOn.Offered))
        {
            var existing = await db.AddOnDefinitions.FirstOrDefaultAsync(candidate => candidate.Id == quota.Id, ct);
            if (existing is null)
            {
                var created = SubscriptionAddOnCatalogSeeder.BuildQuotaDefinition(quota, DateTime.UtcNow);
                db.AddOnDefinitions.Add(created);
                await AuditAddOnAsync(audit, "addon.created", created, before: null, ct);
                changed.Add(quota.Code);
                continue;
            }
            // Los quota add-ons existen desde CAT-F3 con AllowMultipleInstances=false (un tier por tenant).
            // Cambiamos la semántica a "N packs del mismo tier apilables" (patrón Google Workspace / Microsoft
            // OneDrive): el seeder los crea con true, y acá se sincroniza en los tenants que ya tenían el
            // catálogo seedeado antes del cambio. El MutexGroup sigue impidiendo mezclar tiers.
            if (!existing.AllowMultipleInstances)
            {
                var before = CatalogAuditViews.Of(existing);
                existing.SetAllowMultipleInstances(true);
                await AuditAddOnAsync(audit, "addon.allow_multiple", existing, before, ct);
                changed.Add($"{quota.Code}:allow-multi");
            }
        }

        if (changed.Count == 0)
            return;

        await unitOfWork.SaveChangesAsync(ct);
        log.LogInformation("Catalog reconciler synchronized quota add-ons: {Codes}.", string.Join(", ", changed));
    }

    private const string ReconcilerActorName = "reconciler";

    private static readonly CatalogAuditActor Reconciler = CatalogAuditActor.System(ReconcilerActorName);

    private static Task AuditAddOnAsync(
        ICatalogAuditLog audit,
        string action,
        AddOnDefinition addOn,
        object? before,
        CancellationToken ct
    ) =>
        CatalogAudit.RecordAsync(
            audit,
            Reconciler,
            action,
            CatalogAuditViews.Target(addOn),
            before,
            CatalogAuditViews.Of(addOn),
            reason: null,
            Reconciler.Label,
            DateTime.UtcNow,
            ct
        );
}
