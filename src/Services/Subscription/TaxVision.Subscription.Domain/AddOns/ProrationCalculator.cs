using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.AddOns;

/// <summary>
/// Prorratea, por días, el cargo del período parcial inicial de un add-on comprado a mitad del
/// período de la base. Puro (sin dependencias). La suscripción base NUNCA se proratea: esto aplica
/// solo a add-ons.
/// </summary>
public static class ProrationCalculator
{
    public static Result<Money> InitialPeriod(
        Money fullPeriodPrice,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        DateTime chargeStartUtc
    )
    {
        if (periodEndUtc <= periodStartUtc)
            return Result.Failure<Money>(
                new Error("Proration.InvalidPeriod", "Period end must be after period start.")
            );

        // Comprado al inicio (o antes) del período: se cobra el período completo.
        if (chargeStartUtc <= periodStartUtc)
            return Result.Success(fullPeriodPrice);

        // Ya no queda período que cobrar.
        if (chargeStartUtc >= periodEndUtc)
            return Result.Success(Money.Zero(fullPeriodPrice.Currency));

        var totalDays = (decimal)(periodEndUtc - periodStartUtc).TotalDays;
        var remainingDays = (decimal)(periodEndUtc - chargeStartUtc).TotalDays;
        var prorated = decimal.Round(
            fullPeriodPrice.Amount * remainingDays / totalDays,
            2,
            MidpointRounding.AwayFromZero
        );
        return Money.Create(prorated, fullPeriodPrice.Currency);
    }
}
