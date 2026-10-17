using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.RateLimiting.Queries;

public static class GetPlanRateLimitsByPlanHandler
{
    public static async Task<Result<IReadOnlyList<PlanRateLimitResponse>>> Handle(
        GetPlanRateLimitsByPlanQuery query,
        IPlanRateLimitRepository repository,
        CancellationToken ct
    )
    {
        var rows = await repository.GetByPlanCodeAsync(query.PlanCode, ct);
        var response = rows.Select(row => new PlanRateLimitResponse(
                row.PlanCode.Value,
                row.Category.ToString(),
                row.MultiplierOverride,
                row.HardOverridePerMinute
            ))
            .ToArray();
        return Result.Success<IReadOnlyList<PlanRateLimitResponse>>(response);
    }
}
