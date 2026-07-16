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
                customDays ?? throw new InvalidOperationException("Custom billing cycle requires customDays.")),
            _ => throw new ArgumentOutOfRangeException(nameof(cycle), cycle, "Unsupported billing cycle."),
        };
}
