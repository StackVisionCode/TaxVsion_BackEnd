using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Plans.Commands.SetPlanPrices;

public static class SetPlanPricesHandler
{
    // Cambiar el precio no altera entitlements, así que a diferencia de SetPlanModules no encola recálculo.
    public static async Task<Result> Handle(
        SetPlanPricesCommand command,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdForUpdateAsync(command.PlanId, ct);
        if (plan is null)
            return Result.Failure(new Error("Plan.NotFound", "Plan not found."));

        var revised = plan.RevisePrices(command.MonthlyUsd, command.YearlyUsd, command.ActorUserId, DateTime.UtcNow);
        if (revised.IsFailure)
            return revised;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
