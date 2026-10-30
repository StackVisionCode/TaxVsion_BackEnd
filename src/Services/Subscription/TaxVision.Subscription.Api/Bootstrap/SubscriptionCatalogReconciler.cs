using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForPlan;
using TaxVision.Subscription.Application.Plans.Commands.SetPlanModules;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Configuration;
using TaxVision.Subscription.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Subscription.Api.Bootstrap;

public sealed class SubscriptionCatalogReconciler(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<SubscriptionCatalogReconciler> logger,
    IOptions<SubscriptionCatalogOptions> catalogOptions
) : DeferredStartupHostedService(lifetime, logger)
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var log = services.GetRequiredService<ILogger<SubscriptionCatalogReconciler>>();
        var db = services.GetRequiredService<SubscriptionDbContext>();

        await SubscriptionModuleCatalogSeeder.SeedAsync(db, catalogOptions.Value, log, cancellationToken);
        await ReconcilePlanModulesAsync(services, log, cancellationToken);
        await ReconcileCommunicationEntitlementsAsync(services, log, cancellationToken);
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
                new SetPlanModulesCommand(plan.Id, selectedCommercial, Guid.Empty),
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

    // Solo los tres planes comerciales: un testlive* u otro plan ad hoc no recibe claves que no eligió.
    private static readonly string[] CommunicationReconciledPlanCodes =
    [
        PlanCatalog.Starter,
        PlanCatalog.Pro,
        PlanCatalog.Enterprise,
    ];

    // Idempotente: solo versiona el plan cuya versión publicada no trae todas las claves communication.*;
    // el recálculo por plan propaga la versión nueva a cada tenant vía entitlements_changed.
    private static async Task ReconcileCommunicationEntitlementsAsync(
        IServiceProvider services,
        ILogger log,
        CancellationToken ct
    )
    {
        var plans = services.GetRequiredService<IPlanRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var bus = services.GetRequiredService<IMessageBus>();

        foreach (var snapshot in await plans.GetAllAsync(ct))
        {
            if (!CommunicationReconciledPlanCodes.Contains(snapshot.Code.Value, StringComparer.Ordinal))
                continue;

            var published = snapshot.GetPublishedVersion();
            if (published is null)
            {
                log.LogWarning(
                    "Catalog reconciler skipped communication entitlements for plan {PlanCode}: no published version.",
                    snapshot.Code.Value
                );
                continue;
            }

            if (CommunicationEntitlementCatalog.MissingFrom(published, snapshot.Tier).Count == 0)
                continue;

            var plan = await plans.GetByIdForUpdateAsync(snapshot.Id, ct);
            if (plan is null)
                continue;

            var result = plan.AddMissingEntitlements(
                CommunicationEntitlementCatalog.SeedsFor(plan.Tier),
                Guid.Empty,
                DateTime.UtcNow
            );
            if (result.IsFailure)
            {
                log.LogError(
                    "Catalog reconciler could not add communication entitlements to plan {PlanCode}: {ErrorCode}.",
                    plan.Code.Value,
                    result.Error.Code
                );
                continue;
            }

            if (result.Value == 0)
                continue;

            await unitOfWork.SaveChangesAsync(ct);
            await bus.PublishAsync(new RecalculateEntitlementsForPlanCommand(plan.Id));
            log.LogInformation(
                "Catalog reconciler added {Count} communication entitlements to plan {PlanCode}.",
                result.Value,
                plan.Code.Value
            );
        }
    }

    private static async Task ReconcileQuotaAddOnsAsync(IServiceProvider services, ILogger log, CancellationToken ct)
    {
        var db = services.GetRequiredService<SubscriptionDbContext>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var changed = new List<string>();

        foreach (var quota in QuotaAddOnCatalog.All.Where(addOn => addOn.Offered))
        {
            var existing = await db.AddOnDefinitions.FirstOrDefaultAsync(candidate => candidate.Id == quota.Id, ct);
            if (existing is null)
            {
                db.AddOnDefinitions.Add(SubscriptionAddOnCatalogSeeder.BuildQuotaDefinition(quota, DateTime.UtcNow));
                changed.Add(quota.Code);
                continue;
            }
            // Los quota add-ons existen desde CAT-F3 con AllowMultipleInstances=false (un tier por tenant).
            // Cambiamos la semántica a "N packs del mismo tier apilables" (patrón Google Workspace / Microsoft
            // OneDrive): el seeder los crea con true, y acá se sincroniza en los tenants que ya tenían el
            // catálogo seedeado antes del cambio. El MutexGroup sigue impidiendo mezclar tiers.
            if (!existing.AllowMultipleInstances)
            {
                existing.SetAllowMultipleInstances(true);
                changed.Add($"{quota.Code}:allow-multi");
            }
        }

        if (changed.Count == 0)
            return;

        await unitOfWork.SaveChangesAsync(ct);
        log.LogInformation("Catalog reconciler synchronized quota add-ons: {Codes}.", string.Join(", ", changed));
    }
}
