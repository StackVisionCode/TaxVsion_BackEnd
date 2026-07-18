using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Application.Plans.Queries;

public static class GetPlansHandler
{
    public static async Task<Result<IReadOnlyList<PlanResponse>>> Handle(
        GetPlansQuery query,
        IPlanRepository plans,
        CancellationToken ct
    )
    {
        var published = await plans.GetPublishedAsync(ct);

        var response = new List<PlanResponse>(published.Count);
        foreach (var plan in published)
        {
            var mapped = MapToResponse(plan);
            if (mapped is not null)
                response.Add(mapped);
        }

        return Result.Success<IReadOnlyList<PlanResponse>>(response);
    }

    private static PlanResponse? MapToResponse(SubscriptionPlan plan)
    {
        var version = plan.GetPublishedVersion();
        if (version is null)
            return null;

        return new PlanResponse(
            plan.Id,
            plan.Code.Value,
            plan.Name,
            plan.Description,
            plan.Tier.ToString(),
            PlanVersionEntitlements.GetMonthlyPriceUsd(version),
            version.SupportedBillingCycles.Select(cycle => cycle.ToString()).ToArray(),
            PlanVersionEntitlements.GetPricesUsdByCycle(version),
            PlanVersionEntitlements.GetInt(version, "seats.max", fallback: 0),
            PlanVersionEntitlements.GetInt(version, "invitations.max_pending", fallback: 0),
            PlanVersionEntitlements.GetLong(version, "storage.max_bytes", fallback: 0),
            PlanVersionEntitlements.GetEnabledModules(version)
        );
    }
}
