using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.AddOns;

public static class AddOnPriceTierFactory
{
    public static Result<IReadOnlyCollection<AddOnPriceTier>> Create(
        Guid definitionId,
        decimal monthlyUsd,
        decimal yearlyUsd
    )
    {
        var monthly = Money.Create(monthlyUsd, "USD");
        if (monthly.IsFailure)
            return Result.Failure<IReadOnlyCollection<AddOnPriceTier>>(monthly.Error);

        var yearly = Money.Create(yearlyUsd, "USD");
        if (yearly.IsFailure)
            return Result.Failure<IReadOnlyCollection<AddOnPriceTier>>(yearly.Error);

        return Result.Success<IReadOnlyCollection<AddOnPriceTier>>([
            AddOnPriceTier.Create(definitionId, BillingCycle.Monthly, 1, null, monthly.Value).Value,
            AddOnPriceTier.Create(definitionId, BillingCycle.Yearly, 1, null, yearly.Value).Value,
        ]);
    }
}
