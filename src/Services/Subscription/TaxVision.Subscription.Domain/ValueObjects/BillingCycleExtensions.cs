namespace TaxVision.Subscription.Domain.ValueObjects;

public static class BillingCycleExtensions
{
    public static DateTime CalculateNext(this BillingCycle cycle, DateTime fromUtc, int? customDays = null) =>
        cycle switch
        {
            BillingCycle.Monthly => fromUtc.AddMonths(1),
            BillingCycle.Quarterly => fromUtc.AddMonths(3),
            BillingCycle.Yearly => fromUtc.AddYears(1),
            BillingCycle.Custom => fromUtc.AddDays(
                customDays ?? throw new InvalidOperationException("Custom billing cycle requires customDays.")
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(cycle), cycle, "Unsupported billing cycle."),
        };

    public static int DaysInPeriod(this BillingCycle cycle, DateTime periodStartUtc, DateTime periodEndUtc) =>
        (int)(periodEndUtc - periodStartUtc).TotalDays;

    public static decimal ProrationFactor(
        this BillingCycle cycle,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        DateTime effectiveDateUtc
    )
    {
        var totalDays = cycle.DaysInPeriod(periodStartUtc, periodEndUtc);
        if (totalDays <= 0)
            return 0m;

        var remainingDays = (int)(periodEndUtc - effectiveDateUtc).TotalDays;
        if (remainingDays < 0)
            remainingDays = 0;
        if (remainingDays > totalDays)
            remainingDays = totalDays;

        return Math.Round((decimal)remainingDays / totalDays, 6, MidpointRounding.AwayFromZero);
    }
}
