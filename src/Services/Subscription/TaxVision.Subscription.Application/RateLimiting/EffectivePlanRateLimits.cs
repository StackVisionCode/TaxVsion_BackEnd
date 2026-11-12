using BuildingBlocks.RateLimiting;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.RateLimiting;

namespace TaxVision.Subscription.Application.RateLimiting;

/// <summary>Cupo de un plan para una categoría, con su origen.</summary>
public sealed record EffectivePlanRateLimit(
    string PlanCode,
    RateLimitCategory Category,
    decimal Multiplier,
    int? HardOverridePerMinute,
    bool FromDefaultProfile
);

/// <summary>
/// D12: el catálogo de rate limits que lee la flota. Cada plan que existe tiene un valor para cada categoría con
/// tenant: su fila si la tiene y, si no, el perfil por defecto. Así ningún plan cae al cupo base por omisión y los
/// lectores (HttpPlanRateLimitReader en BuildingBlocks, el resolver de Node) no cambian.
/// </summary>
public static class EffectivePlanRateLimits
{
    public static async Task<IReadOnlyList<EffectivePlanRateLimit>> BuildAsync(
        IPlanRepository plans,
        IPlanRateLimitRepository rows,
        IRateLimitDefaultProfileRepository profiles,
        CancellationToken ct
    )
    {
        var existing = await rows.GetAllAsync(ct);
        var result = existing
            .Select(row => new EffectivePlanRateLimit(
                row.PlanCode.Value,
                row.Category,
                row.MultiplierOverride,
                row.HardOverridePerMinute,
                FromDefaultProfile: false
            ))
            .ToList();

        var profile = await profiles.GetAsync(ct);
        if (profile is null)
            return result;

        var present = result.Select(entry => (entry.PlanCode, entry.Category)).ToHashSet();
        foreach (var plan in await plans.GetAllAsync(ct))
        foreach (var category in RateLimitDefaultProfile.ScalableCategories)
        {
            if (present.Contains((plan.Code.Value, category)))
                continue;
            result.Add(
                new EffectivePlanRateLimit(
                    plan.Code.Value,
                    category,
                    profile.MultiplierFor(category).Value,
                    HardOverridePerMinute: null,
                    FromDefaultProfile: true
                )
            );
        }

        return result;
    }
}
