using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.AddOns.TierChange;

/// <summary>Todo lo que hace falta para decidir un cambio de tamaño, ya validado.</summary>
public sealed record AddOnTierChangeContext(
    TenantAddOn AddOn,
    TenantSubscription Subscription,
    AddOnDefinition Current,
    AddOnDefinition Target,
    AddOnTierChangePlan Plan
);

/// <summary>Carga el add-on, la suscripción y los dos tamaños, y arma el plan del cambio. Lo comparten la
/// vista previa, el cambio y el checkout de la subida.</summary>
public sealed class AddOnTierChangeLoader(
    ITenantAddOnRepository tenantAddOns,
    ISubscriptionRepository subscriptions,
    IAddOnDefinitionRepository definitions
)
{
    public async Task<Result<AddOnTierChangeContext>> LoadAsync(
        Guid tenantId,
        Guid tenantAddOnId,
        string targetCode,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var addOn = await tenantAddOns.GetByIdAsync(tenantAddOnId, tenantId, ct);
        if (addOn is null)
            return Fail("AddOn.NotFound", "Add-on does not exist.");

        var subscription = await subscriptions.GetByTenantIdAsync(tenantId, ct);
        if (subscription is null)
            return Fail("Subscription.NotFound", "Subscription does not exist.");

        var allowed = AddOnPurchasePolicy.EnsureSubscriptionAllows(subscription);
        if (allowed.IsFailure)
            return Result.Failure<AddOnTierChangeContext>(allowed.Error);

        var current = await definitions.GetByIdAsync(addOn.AddOnDefinitionId, ct);
        var target = await definitions.GetByCodeAsync(targetCode.Trim(), ct);
        if (current is null || target is null)
            return Fail("AddOn.TierNotFound", "That size does not exist.");

        var instances = await CountActiveInGroupAsync(tenantId, current, ct);
        var plan = AddOnTierChangePolicy.Plan(addOn, current, target, instances, nowUtc);
        return plan.IsFailure
            ? Result.Failure<AddOnTierChangeContext>(plan.Error)
            : Result.Success(new AddOnTierChangeContext(addOn, subscription, current, target, plan.Value));
    }

    // Paquetes vivos del mismo grupo exclusivo: con más de uno, cambiar el tamaño de uno no es posible.
    private async Task<int> CountActiveInGroupAsync(Guid tenantId, AddOnDefinition current, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(current.MutexGroup))
            return 1;

        var groupCodes = (await definitions.GetPublishedAsync(ct))
            .Where(d => string.Equals(d.MutexGroup, current.MutexGroup, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Code.Value)
            .Append(current.Code.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return (await tenantAddOns.GetByTenantIdAsync(tenantId, ct)).Count(a =>
            a.Status is AddOnStatus.Active or AddOnStatus.PastDue && groupCodes.Contains(a.AddOnCode)
        );
    }

    private static Result<AddOnTierChangeContext> Fail(string code, string message) =>
        Result.Failure<AddOnTierChangeContext>(new Error(code, message));
}
