using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Subscriptions.Queries;

public static class GetPendingPlanChangeHandler
{
    public static async Task<Result<PendingPlanChangeResponse?>> Handle(
        GetPendingPlanChangeQuery query,
        ISubscriptionRepository subscriptions,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(query.TenantId, ct);
        if (subscription is null)
            return Result.Failure<PendingPlanChangeResponse?>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        return Result.Success(PendingPlanChangeProjection.From(subscription));
    }
}
