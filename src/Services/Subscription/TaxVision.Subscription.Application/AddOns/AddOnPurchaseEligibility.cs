using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.AddOns;

/// <summary>
/// Si este tenant puede comprar este add-on ahora. Lo comparten los DOS caminos de compra — off-session
/// (<c>POST /addons</c>) y checkout hosteado (<c>POST /addons/checkout</c>) — para que no se pueda esquivar un
/// guard eligiendo el otro camino.
/// </summary>
public static class AddOnPurchaseEligibility
{
    private static readonly SubscriptionStatus[] PurchasableStatuses =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active,
    ];

    public static async Task<
        Result<(TenantSubscription Subscription, AddOnDefinition Definition)>
    > EnsurePurchasableAsync(
        Guid tenantId,
        string? addOnCode,
        int quantity,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IModuleRepository modules,
        IAddOnDefinitionRepository addOnDefinitions,
        ISubscriptionTenantSettingsRepository settingsRepository,
        ITenantAddOnRepository tenantAddOns,
        CancellationToken ct
    )
    {
        if (quantity < 1)
            return Fail("AddOn.InvalidQuantity", "Quantity must be at least 1.");

        var subscription = await subscriptions.GetByTenantIdAsync(tenantId, ct);
        if (subscription is null)
            return Fail("Subscription.NotFound", "Subscription does not exist.");

        if (Array.IndexOf(PurchasableStatuses, subscription.Status) < 0)
            return Fail(
                "Subscription.CannotPurchaseAddOns",
                $"Cannot purchase add-ons while subscription is {subscription.Status}."
            );

        var settings = await settingsRepository.GetByTenantIdAsync(tenantId, ct);
        if (settings is not null && !settings.AllowAddons)
            return Fail("AddOn.NotAllowed", "This tenant does not allow add-on purchases.");

        var definition = await addOnDefinitions.GetByCodeAsync(
            addOnCode?.Trim().ToLowerInvariant() ?? string.Empty,
            ct
        );
        if (definition is null || definition.Status != AddOnDefinitionStatus.Published)
            return Fail("AddOnDefinition.NotFound", "Add-on does not exist.");

        if (string.Equals(definition.Category, "module", StringComparison.OrdinalIgnoreCase))
        {
            var moduleCodes = definition
                .Features.Where(feature =>
                    feature.Enabled && feature.FeatureKey.Value.StartsWith("module.", StringComparison.Ordinal)
                )
                .Select(feature => feature.FeatureKey.Value["module.".Length..])
                .Take(2)
                .ToArray();
            var module = moduleCodes.Length == 1 ? await modules.GetByCodeAsync(moduleCodes[0], ct) : null;
            if (module is null || !module.IsSellableAsAddon || definition.Code.Value != module.AddOnCode)
                return Fail("AddOnDefinition.NotFound", "Add-on does not exist.");
        }

        // C6 — no cobrar por algo que el plan ya trae ni por el mismo add-on dos veces.
        var owned = await tenantAddOns.GetByTenantIdAsync(tenantId, ct);
        if (AddOnEligibilityRules.AlreadyOwned(definition, owned))
            return Fail("AddOn.AlreadyActive", "This add-on is already active for your office.");

        // CAT-F3.4 — guard mutex: dos add-ons del mismo MutexGroup no coexisten (p.ej. storage small
        // y storage large). Para cambiar de tamaño hay que cancelar el actual primero. Hacerlo acá
        // en lugar de en el aggregate TenantAddOn porque necesita leer el catálogo (MutexGroup vive
        // en AddOnDefinition, no en TenantAddOn), y ambos handlers (off-session y checkout) deben
        // ver el guard sin duplicar la consulta.
        if (definition.MutexGroup is not null)
        {
            foreach (var ownedAddOn in owned)
            {
                if (ownedAddOn.Status != AddOnStatus.Active)
                    continue;
                if (string.Equals(ownedAddOn.AddOnCode, definition.Code.Value, StringComparison.OrdinalIgnoreCase))
                    continue;

                var ownedDefinition = await addOnDefinitions.GetByCodeAsync(ownedAddOn.AddOnCode, ct);
                if (ownedDefinition?.MutexGroup is null)
                    continue;
                if (
                    !string.Equals(
                        ownedDefinition.MutexGroup,
                        definition.MutexGroup,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    continue;

                return Fail(
                    "AddOn.MutexGroupConflict",
                    $"Cannot purchase this add-on while '{ownedAddOn.AddOnCode}' is active (same group '{definition.MutexGroup}'). Cancel it first."
                );
            }
        }

        var enabledModules = await EnabledModulesAsync(subscription, plans, ct);
        if (AddOnEligibilityRules.IsIncludedInPlan(definition, enabledModules))
            return Fail("AddOn.AlreadyIncludedInPlan", "Your plan already includes this add-on.");

        return Result.Success((subscription, definition));
    }

    /// <summary>
    /// Módulos de la versión PUBLICADA del plan — la misma que usa <c>EntitlementSnapshotBuilder</c>,
    /// que es la que concede el acceso de verdad.
    ///
    /// Antes leía la versión contratada, y eso vendía módulos que el tenant ya tenía: al revisar un
    /// plan, el recálculo le daba el módulo nuevo (el gate lo dejaba entrar) pero este guard seguía
    /// mirando la versión vieja y no veía el módulo, así que el add-on aparecía comprable. El tenant
    /// habría pagado por algo que ya estaba usando.
    /// </summary>
    private static async Task<IReadOnlyList<string>> EnabledModulesAsync(
        TenantSubscription subscription,
        IPlanRepository plans,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        var version =
            plan?.GetPublishedVersion()
            ?? plan?.Versions.FirstOrDefault(candidate => candidate.Id == subscription.PlanVersionId);
        return version is null ? [] : PlanVersionEntitlements.GetEnabledModules(version);
    }

    private static Result<(TenantSubscription, AddOnDefinition)> Fail(string code, string message) =>
        Result.Failure<(TenantSubscription, AddOnDefinition)>(new Error(code, message));
}
