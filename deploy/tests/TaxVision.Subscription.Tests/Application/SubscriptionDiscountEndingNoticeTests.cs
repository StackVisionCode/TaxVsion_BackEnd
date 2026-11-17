using BuildingBlocks.TimeZones;
using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// Un descuento de N meses avisa al owner una vez, 7 días antes de terminar, a las 9:00 de la oficina. Los de un solo
/// cobro y los de para siempre no avisan.
/// </summary>
public sealed class SubscriptionDiscountEndingNoticeTests
{
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");
    private static readonly DateTime Jan1 = new(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Termina el 1-abr 00:00Z (31-mar en Nueva York); el 24-mar a las 9:00 EDT (13:00Z) faltan 7 días.
    private static readonly DateTime SevenDaysBeforeAtNine = new(2027, 3, 24, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_months_discount_is_announced_seven_days_before_it_ends()
    {
        var (subscription, discount) = Granted(DiscountDuration.Months, months: 3);

        var notice = SubscriptionDiscountEndingNotice.For(discount, SevenDaysBeforeAtNine, NewYork, "corr-1");

        Assert.NotNull(notice);
        Assert.Equal(subscription.TenantId, notice.TenantId);
        Assert.Equal(subscription.Id, notice.TenantSubscriptionId);
        Assert.Equal(discount.Id, notice.DiscountId);
        Assert.Equal(Jan1.AddMonths(3), notice.EndsAtUtc);
        Assert.Equal(7, notice.DaysUntilEnd);
        Assert.Equal(2_000, notice.PercentBasisPoints);
        Assert.Equal("Admin", notice.Source);
        Assert.Equal("corr-1", notice.CorrelationId);
    }

    [Fact]
    public void No_notice_outside_nine_or_on_another_day()
    {
        var (_, discount) = Granted(DiscountDuration.Months, months: 3);

        Assert.Null(SubscriptionDiscountEndingNotice.For(discount, SevenDaysBeforeAtNine.AddHours(1), NewYork, "c"));
        Assert.Null(SubscriptionDiscountEndingNotice.For(discount, SevenDaysBeforeAtNine.AddDays(1), NewYork, "c"));
    }

    [Theory]
    [InlineData(DiscountDuration.Forever)]
    [InlineData(DiscountDuration.Once)]
    public void Forever_and_one_time_discounts_are_not_announced(DiscountDuration duration)
    {
        var (_, discount) = Granted(duration, months: null);
        // Un "Once" termina el 2-ene 00:00Z: el 25-dic a las 9:00 EST (14:00Z) faltarían 7 días.
        var sevenDaysBefore = new DateTime(2026, 12, 25, 14, 0, 0, DateTimeKind.Utc);

        Assert.Null(SubscriptionDiscountEndingNotice.For(discount, sevenDaysBefore, NewYork, "c"));
        Assert.Null(SubscriptionDiscountEndingNotice.For(discount, SevenDaysBeforeAtNine, NewYork, "c"));
    }

    private static (TenantSubscription, SubscriptionDiscount) Granted(DiscountDuration duration, int? months)
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("pro").Value, "pro", "pro plan", PlanTier.Starter, Guid.Empty, Jan1)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
        plan.AddVersion(version, Guid.Empty, Jan1);
        plan.PublishVersion(version.Id, Jan1, Guid.Empty, Jan1);
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                Jan1,
                Jan1.AddMonths(1),
                Guid.Empty,
                Jan1
            )
            .Value;
        var terms = SubscriptionDiscountTerms
            .Create(
                DiscountSource.Admin,
                null,
                2_000,
                null,
                null,
                duration,
                months,
                DiscountAppliesTo.Base,
                null,
                "Retention deal",
                Jan1
            )
            .Value;
        var discount = subscription.GrantDiscount(terms, replace: false, Guid.Empty, Jan1).Value;
        return (subscription, discount);
    }
}
