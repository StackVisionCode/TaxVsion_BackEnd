using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

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

        var pending = subscription.PlanChangeRequests.FirstOrDefault(r => r.Status == PlanChangeRequestStatus.Pending);
        if (pending is null)
            return Result.Success<PendingPlanChangeResponse?>(null);

        return Result.Success<PendingPlanChangeResponse?>(
            new PendingPlanChangeResponse(
                pending.Id,
                pending.FromPlanCode,
                pending.ToPlanCode,
                pending.EffectiveMode.ToString(),
                pending.EffectiveAtUtc,
                pending.RequestedAtUtc
            )
        );
    }
}
