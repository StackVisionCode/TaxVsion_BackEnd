using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Plans.Queries;

public sealed record GetInternalPlanSummaryQuery(Guid PlanId);

public sealed record InternalPlanSummaryResponse(Guid Id, string Code, string Name);

/// <summary>
/// Nombre de un plan para otros servicios (recibos y correos de Auth), esté o no a la venta. El listado público
/// solo trae lo que se vende, así que un plan retirado ya no aparece ahí.
/// </summary>
public static class GetInternalPlanSummaryHandler
{
    public static async Task<Result<InternalPlanSummaryResponse>> Handle(
        GetInternalPlanSummaryQuery query,
        IPlanRepository plans,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdAsync(query.PlanId, ct);
        if (plan is null)
            return Result.Failure<InternalPlanSummaryResponse>(
                new Error("Subscription.Plan.NotFound", "The plan does not exist.")
            );

        return Result.Success(new InternalPlanSummaryResponse(plan.Id, plan.Code.Value, plan.Name));
    }
}
