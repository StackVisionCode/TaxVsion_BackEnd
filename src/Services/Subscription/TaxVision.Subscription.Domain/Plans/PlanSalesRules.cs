using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Reglas de venta que miran más de un plan. El handler junta los datos (cuántos otros planes están a la venta y
/// cuál es el plan del trial) y la regla decide.
/// </summary>
public static class PlanSalesRules
{
    public static Result EnsureCanWithdraw(SubscriptionPlan plan, int otherPlansOnSale, string trialPlanCode)
    {
        if (!plan.IsOnSale)
            return Result.Success();

        // Sin ningún plan a la venta nadie podría registrarse.
        if (otherPlansOnSale <= 0)
            return Result.Failure(
                new Error("Plan.LastOnSale", "At least one plan must stay on sale. Put another plan on sale first.")
            );

        // Las oficinas nuevas arrancan su trial en este plan.
        if (string.Equals(plan.Code.Value, trialPlanCode, StringComparison.OrdinalIgnoreCase))
            return Result.Failure(
                new Error("Plan.IsTrialPlan", "New offices start their trial on this plan, so it must stay on sale.")
            );

        return Result.Success();
    }
}
