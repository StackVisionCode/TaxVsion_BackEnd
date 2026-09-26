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
        SubscriptionStatus.GracePeriod,
    ];

    public static async Task<
        Result<(TenantSubscription Subscription, AddOnDefinition Definition)>
    > EnsurePurchasableAsync(
        Guid tenantId,
        string? addOnCode,
        int quantity,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
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

        // C6 — no cobrar por algo que el plan ya trae ni por el mismo add-on dos veces.
        var owned = await tenantAddOns.GetByTenantIdAsync(tenantId, ct);
        if (AddOnEligibilityRules.AlreadyOwned(definition, owned))
            return Fail("AddOn.AlreadyActive", "This add-on is already active for your office.");

        var enabledModules = await EnabledModulesAsync(subscription, plans, ct);
        if (AddOnEligibilityRules.IsIncludedInPlan(definition, enabledModules))
            return Fail("AddOn.AlreadyIncludedInPlan", "Your plan already includes this add-on.");

        return Result.Success((subscription, definition));
    }

    /// <summary>Módulos de la versión CONTRATADA del plan; si ya no está, la publicada.</summary>
    private static async Task<IReadOnlyList<string>> EnabledModulesAsync(
        TenantSubscription subscription,
        IPlanRepository plans,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        var version =
            plan?.Versions.FirstOrDefault(candidate => candidate.Id == subscription.PlanVersionId)
            ?? plan?.GetPublishedVersion();
        return version is null ? [] : PlanVersionEntitlements.GetEnabledModules(version);
    }

    private static Result<(TenantSubscription, AddOnDefinition)> Fail(string code, string message) =>
        Result.Failure<(TenantSubscription, AddOnDefinition)>(new Error(code, message));
}
