using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Plans.Commands.SetPlanModules;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Subscription.Api.Bootstrap;

/// <summary>
/// Pone el catálogo vivo (planes y add-ons) al día con el catálogo del código, en cada arranque.
///
/// <para><b>Por qué hace falta.</b> Los dos seeders del catálogo empiezan con
/// <c>if (await db.X.AnyAsync(ct)) return;</c>, así que solo actúan contra una base vacía: editar el
/// seeder cambia una instalación nueva y **no toca producción**. Este servicio es el otro camino, el de
/// la instalación que ya existe.</para>
///
/// <para><b>Por qué no una migración.</b> Un <c>UPDATE</c> por SQL cambiaría las filas sin publicar una
/// versión nueva del plan (las suscripciones apuntan a un <c>PlanVersionId</c> concreto) y sin disparar
/// el recálculo de entitlements. Los 24 servicios se quedarían con su proyección vieja y el gate de
/// módulo seguiría aplicando los módulos anteriores — una incoherencia silenciosa justo en la capa que
/// decide quién entra.</para>
///
/// <para><b>Cómo lo hace.</b> Manda el mismo <see cref="SetPlanModulesCommand"/> que manda el endpoint
/// de administración: misma puerta de dominio, mismo handler, misma publicación de
/// <c>RecalculateEntitlementsForPlanCommand</c>. Lo único distinto es quién la abre — allí un
/// PlatformAdmin con un clic, aquí el propio servicio tras el despliegue.</para>
///
/// <para>🔑 <b>Compara antes de actuar.</b> <see cref="SubscriptionPlan.ReviseModules"/> publica una
/// versión nueva <b>incondicionalmente</b> (<c>published.VersionNumber + 1</c>), sin mirar si los
/// módulos cambiaron. Sin esta comparación, cada reinicio publicaría v2, v3, v4… y cada una dispararía
/// un recálculo masivo de todos los tenants del plan. En producción, con reinicios por despliegue o por
/// escalado, sería un goteo permanente de versiones basura y de eventos.</para>
/// </summary>
public sealed class SubscriptionCatalogReconciler(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<SubscriptionCatalogReconciler> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<SubscriptionCatalogReconciler>>();

        await ReconcilePlanModulesAsync(scope.ServiceProvider, log, cancellationToken);
        await ReconcileAddOnsAsync(scope.ServiceProvider, log, cancellationToken);
    }

    private static async Task ReconcilePlanModulesAsync(IServiceProvider services, ILogger log, CancellationToken ct)
    {
        var plans = services.GetRequiredService<IPlanRepository>();
        var bus = services.GetRequiredService<IMessageBus>();

        foreach (var (planId, planCode, desiredModules) in PlanModuleCatalog.All)
        {
            var plan = await plans.GetByIdAsync(planId, ct);
            if (plan is null)
            {
                // Base todavía sin sembrar (arranque en frío): el seeder ya la dejará con el catálogo
                // correcto, no hay nada que reconciliar.
                log.LogDebug("Catalog reconciler: plan {PlanCode} not seeded yet; skipping.", planCode);
                continue;
            }

            var current = PublishedModulesOf(plan);
            if (current is null)
            {
                log.LogWarning("Catalog reconciler: plan {PlanCode} has no published version.", planCode);
                continue;
            }

            if (!NeedsRevision(current, desiredModules))
                continue;

            var result = await bus.InvokeAsync<BuildingBlocks.Results.Result>(
                new SetPlanModulesCommand(planId, [.. desiredModules], ActorUserId: Guid.Empty),
                ct
            );

            if (result.IsFailure)
            {
                log.LogError(
                    "Catalog reconciler: could not revise modules of plan {PlanCode}: {Code}.",
                    planCode,
                    result.Error.Code
                );
                continue;
            }

            log.LogInformation(
                "Catalog reconciler: plan {PlanCode} modules [{Before}] -> [{After}]; mass recalculation queued.",
                planCode,
                string.Join(", ", current.Order(StringComparer.Ordinal)),
                string.Join(", ", desiredModules.Order(StringComparer.Ordinal))
            );
        }
    }

    /// <summary>
    /// ¿Hay que publicar una versión nueva? Solo si el conjunto difiere — el orden y las mayúsculas no
    /// cuentan. Es la comprobación que evita que cada reinicio publique una versión más.
    /// </summary>
    public static bool NeedsRevision(IReadOnlySet<string> current, IReadOnlyList<string> desired) =>
        !current.SetEquals(desired);

    /// <summary>Los <c>module.*</c> de la versión publicada, sin el prefijo. <c>null</c> si no hay versión publicada.</summary>
    public static HashSet<string>? PublishedModulesOf(SubscriptionPlan plan)
    {
        var published = plan.Versions.FirstOrDefault(version => version.Status == PlanVersionStatus.Published);
        if (published is null)
            return null;

        return new HashSet<string>(
            published
                .Features.Select(feature => feature.FeatureKey.Value)
                .Where(key => key.StartsWith("module.", StringComparison.Ordinal))
                .Select(key => key["module.".Length..]),
            StringComparer.OrdinalIgnoreCase
        );
    }

    /// <summary>Pone a la venta lo que falte y retira lo que ya no se ofrece.</summary>
    private static async Task ReconcileAddOnsAsync(IServiceProvider services, ILogger log, CancellationToken ct)
    {
        var db = services.GetRequiredService<SubscriptionDbContext>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        var retired = new List<string>();
        var added = new List<string>();

        // Un add-on NUEVO no llega nunca a una base ya sembrada: el seeder solo actúa si la tabla está
        // vacía. Sin esto, `addon-meetings` existiría en el código y no en producción.
        foreach (var catalogEntry in ModuleAddOnCatalog.All.Where(addOn => addOn.Offered))
        {
            var existing = await db.AddOnDefinitions.FirstOrDefaultAsync(
                candidate => candidate.Id == catalogEntry.Id,
                ct
            );

            if (existing is null)
            {
                db.AddOnDefinitions.Add(SubscriptionAddOnCatalogSeeder.BuildDefinition(catalogEntry, DateTime.UtcNow));
                added.Add(catalogEntry.Code);
                continue;
            }

            // Draft = lo sembró este mismo catálogo cuando la feature no existía; ya existe, se publica.
            // Desde Deprecated no hay camino de vuelta en el dominio de hoy: haría falta un método nuevo.
            if (existing.Status == AddOnDefinitionStatus.Draft)
            {
                var publish = existing.Publish(Guid.Empty, DateTime.UtcNow);
                if (publish.IsSuccess)
                    added.Add(catalogEntry.Code);
            }
        }

        foreach (var catalogEntry in ModuleAddOnCatalog.NotOffered)
        {
            var definition = await db.AddOnDefinitions.FirstOrDefaultAsync(
                candidate => candidate.Id == catalogEntry.Id,
                ct
            );
            if (definition is null || definition.Status != AddOnDefinitionStatus.Published)
                continue;

            // Deprecated ya lo saca de la tienda: `GetPublishedAsync` filtra por Published, así que
            // deja de listarse y de poder comprarse. No se archiva: Archive es terminal y estas
            // features están por llegar.
            var result = definition.Deprecate(Guid.Empty, DateTime.UtcNow);
            if (result.IsFailure)
            {
                log.LogWarning(
                    "Catalog reconciler: could not retire add-on {Code}: {Error}.",
                    catalogEntry.Code,
                    result.Error.Code
                );
                continue;
            }

            retired.Add(catalogEntry.Code);
        }

        if (retired.Count == 0 && added.Count == 0)
            return;

        await unitOfWork.SaveChangesAsync(ct);

        if (added.Count > 0)
            log.LogInformation(
                "Catalog reconciler: put {Count} add-on(s) on sale: {Codes}.",
                added.Count,
                string.Join(", ", added)
            );

        if (retired.Count > 0)
            log.LogInformation(
                "Catalog reconciler: retired {Count} add-on(s) from the store: {Codes}.",
                retired.Count,
                string.Join(", ", retired)
            );
    }
}
