using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.RateLimiting.Queries;

/// <summary>
/// El catálogo que lee la flota por <c>internal/plan-rate-limits</c>: completo para cada plan que existe (D12). Lo
/// que un plan no tiene en fila sale del perfil por defecto y viene marcado con <c>FromDefaultProfile</c>.
/// </summary>
public static class GetPlanRateLimitsHandler
{
    public static async Task<Result<IReadOnlyList<PlanRateLimitResponse>>> Handle(
        GetPlanRateLimitsQuery query,
        IPlanRepository plans,
        IPlanRateLimitRepository repository,
        IRateLimitDefaultProfileRepository profiles,
        CancellationToken ct
    )
    {
        var catalog = await EffectivePlanRateLimits.BuildAsync(plans, repository, profiles, ct);
        return Result.Success<IReadOnlyList<PlanRateLimitResponse>>(catalog.Select(ToResponse).ToArray());
    }

    public static PlanRateLimitResponse ToResponse(EffectivePlanRateLimit entry) =>
        new(
            entry.PlanCode,
            entry.Category.ToString(),
            entry.Multiplier,
            entry.HardOverridePerMinute,
            entry.FromDefaultProfile
        );
}
