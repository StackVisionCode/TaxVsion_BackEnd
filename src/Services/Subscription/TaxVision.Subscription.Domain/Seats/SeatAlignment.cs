using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Seats;

/// <summary>Fin y precio del próximo período de un asiento. <c>AlignsWithoutCharge</c>: el tramo se regala.</summary>
public sealed record SeatPeriodCharge(DateTime PeriodEndUtc, Money Price, bool AlignsWithoutCharge);

/// <summary>
/// Los asientos terminan junto con el plan, como los add-ons. Un asiento con otra fecha cobra el tramo hasta
/// el fin del plan, a su propio precio y por días; nunca más de un período propio. Un tramo menor al mínimo
/// del proveedor no se cobra.
/// </summary>
public static class SeatAlignment
{
    public const long MinimumChargeCents = 50;

    /// <summary>Renovación: el tramo chico alinea el asiento sin cobro.</summary>
    public static SeatPeriodCharge ForRenewal(
        Money unitPrice,
        BillingCycle cycle,
        DateTime periodStartUtc,
        DateTime? planPeriodEndUtc
    )
    {
        var fullEnd = cycle.CalculateNext(periodStartUtc);
        if (Stretch(unitPrice, periodStartUtc, fullEnd, planPeriodEndUtc) is not { } stretch)
            return new SeatPeriodCharge(fullEnd, unitPrice, false);

        return Cents(stretch.Price) < MinimumChargeCents
            ? new SeatPeriodCharge(stretch.EndUtc, Money.Zero(unitPrice.Currency), true)
            : new SeatPeriodCharge(stretch.EndUtc, stretch.Price, false);
    }

    /// <summary>Reactivación: el tramo chico se regala y se paga el período siguiente del plan.</summary>
    public static SeatPeriodCharge ForReactivation(
        Money unitPrice,
        BillingCycle cycle,
        DateTime nowUtc,
        DateTime? planPeriodEndUtc
    )
    {
        var fullEnd = cycle.CalculateNext(nowUtc);
        if (Stretch(unitPrice, nowUtc, fullEnd, planPeriodEndUtc) is not { } stretch)
            return new SeatPeriodCharge(fullEnd, unitPrice, false);

        return Cents(stretch.Price) < MinimumChargeCents
            ? new SeatPeriodCharge(cycle.CalculateNext(stretch.EndUtc), unitPrice, false)
            : new SeatPeriodCharge(stretch.EndUtc, stretch.Price, false);
    }

    // Tramo hasta el fin del plan si cae dentro de un ciclo propio; null si ya coincide o el plan no sirve de ancla.
    private static (DateTime EndUtc, Money Price)? Stretch(
        Money unitPrice,
        DateTime startUtc,
        DateTime fullEndUtc,
        DateTime? planPeriodEndUtc
    )
    {
        if (planPeriodEndUtc is not { } planEnd || planEnd <= startUtc || planEnd >= fullEndUtc)
            return null;

        var prorated = decimal.Round(
            unitPrice.Amount * (decimal)(planEnd - startUtc).TotalDays / (decimal)(fullEndUtc - startUtc).TotalDays,
            2,
            MidpointRounding.AwayFromZero
        );
        return (planEnd, Money.Create(prorated, unitPrice.Currency).Value);
    }

    private static long Cents(Money money) => (long)Math.Round(money.Amount * 100m, MidpointRounding.AwayFromZero);
}
