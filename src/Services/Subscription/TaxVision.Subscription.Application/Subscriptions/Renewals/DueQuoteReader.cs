using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.Renewals;

/// <summary>Lee lo que hace falta para cotizar lo vencido de una oficina: plan, precio, asientos y add-ons.</summary>
public static class DueQuoteReader
{
    public static async Task<Result<(TenantSubscription Subscription, DueQuote Quote)>> ReadAsync(
        Guid tenantId,
        DueSelection? selection,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        ISubscriptionSeatRepository seats,
        ITenantAddOnRepository addOns,
        IAddOnDefinitionRepository definitions,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(tenantId, ct);
        if (subscription is null)
            return Result.Failure<(TenantSubscription, DueQuote)>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        var version = plan is null ? null : PlanPricing.FindVersion(plan, subscription.PlanVersionId);
        var price = version is null
            ? null
            : PlanPricing.ResolveBaseSubscriptionPrice(version, subscription.BillingCycle);
        if (DuePayment.IsPlanOverdue(subscription) && (price is null || price.Value.AmountCents <= 0))
            return Result.Failure<(TenantSubscription, DueQuote)>(
                new Error(
                    "Subscription.RenewalCheckout.NothingToCharge",
                    "This plan has no positive price for the current cycle."
                )
            );

        var seatList = await seats.GetByTenantIdForUpdateAsync(tenantId, ct);
        var addOnList = await addOns.GetByTenantIdForUpdateAsync(tenantId, ct);
        var names = new Dictionary<string, string>();
        foreach (var code in addOnList.Select(addOn => addOn.AddOnCode).Distinct())
        {
            if (await definitions.GetByCodeAsync(code, ct) is { } definition)
                names[code] = definition.Name;
        }

        var quote = DuePayment.Quote(
            subscription,
            (price?.AmountCents ?? 0, price?.Currency ?? "USD"),
            plan?.Name ?? subscription.PlanCode,
            seatList,
            addOnList,
            names,
            selection,
            nowUtc
        );
        return quote.IsFailure
            ? Result.Failure<(TenantSubscription, DueQuote)>(quote.Error)
            : Result.Success((subscription, quote.Value));
    }
}
