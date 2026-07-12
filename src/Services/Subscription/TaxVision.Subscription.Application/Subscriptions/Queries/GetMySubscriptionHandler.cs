using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;

namespace TaxVision.Subscription.Application.Subscriptions.Queries;

public static class GetMySubscriptionHandler
{
    public static async Task<Result<MySubscriptionResponse>> Handle(
        GetMySubscriptionQuery query,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(query.TenantId, ct);
        if (subscription is null)
            return Result.Failure<MySubscriptionResponse>(new Error("Subscription.NotFound", "Subscription does not exist."));

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null)
            return Result.Failure<MySubscriptionResponse>(new Error("Plan.NotFound", "Plan does not exist."));

        var planVersion = plan.GetPublishedVersion();
        if (planVersion is null)
            return Result.Failure<MySubscriptionResponse>(new Error("Plan.NoPublishedVersion", "Plan has no published version."));

        return Result.Success(
            new MySubscriptionResponse(
                plan.Code.Value,
                plan.Name,
                subscription.Status.ToString(),
                subscription.BillingCycle.ToString(),
                PlanVersionEntitlements.GetMonthlyPriceUsd(planVersion),
                PlanVersionEntitlements.GetInt(planVersion, "seats.max", fallback: 0),
                PlanVersionEntitlements.GetInt(planVersion, "invitations.max_pending", fallback: 0),
                PlanVersionEntitlements.GetLong(planVersion, "storage.max_bytes", fallback: 0),
                PlanVersionEntitlements.GetEnabledModules(planVersion),
                subscription.TrialEndsAtUtc,
                subscription.CurrentPeriodStartUtc,
                subscription.CurrentPeriodEndUtc,
                subscription.CancelledAtUtc
            )
        );
    }
}
