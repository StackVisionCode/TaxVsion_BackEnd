using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.RateLimiting.Queries;

/// <summary>Vista admin de un plan: sus filas y, para lo que no tiene, el perfil por defecto (marcado).</summary>
public static class GetPlanRateLimitsByPlanHandler
{
    public static async Task<Result<IReadOnlyList<PlanRateLimitResponse>>> Handle(
        GetPlanRateLimitsByPlanQuery query,
        IPlanRepository plans,
        IPlanRateLimitRepository repository,
        IRateLimitDefaultProfileRepository profiles,
        CancellationToken ct
    )
    {
        var catalog = await EffectivePlanRateLimits.BuildAsync(plans, repository, profiles, ct);
        return Result.Success<IReadOnlyList<PlanRateLimitResponse>>(
            catalog
                .Where(entry => entry.PlanCode == query.PlanCode)
                .OrderBy(entry => entry.Category)
                .Select(GetPlanRateLimitsHandler.ToResponse)
                .ToArray()
        );
    }
}
