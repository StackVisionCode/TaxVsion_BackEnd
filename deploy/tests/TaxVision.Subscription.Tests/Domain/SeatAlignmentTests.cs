using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>
/// Los asientos terminan junto con el plan: el que tiene otra fecha cobra el tramo hasta el fin del plan a su
/// precio; un tramo menor a $0.50 se regala. La reactivación cobra hasta el fin del plan, no un período desde hoy.
/// </summary>
public sealed class SeatAlignmentTests
{
    private static readonly Money Fifteen = Money.Create(15m, "USD").Value;
    private static readonly DateTime Dec3 = new(2026, 12, 3, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Dec16 = new(2026, 12, 16, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_seat_already_aligned_renews_a_full_period_at_its_price()
    {
        var next = SeatAlignment.ForRenewal(Fifteen, BillingCycle.Monthly, Dec16, planPeriodEndUtc: Dec16);

        Assert.Equal(new DateTime(2027, 1, 16, 0, 0, 0, DateTimeKind.Utc), next.PeriodEndUtc);
        Assert.Equal(15m, next.Price.Amount);
        Assert.False(next.AlignsWithoutCharge);
    }

    [Fact]
    public void A_seat_with_another_date_charges_up_to_the_plan_end_at_its_price()
    {
        var next = SeatAlignment.ForRenewal(Fifteen, BillingCycle.Monthly, Dec3, planPeriodEndUtc: Dec16);

        // 13 de 31 días de su ciclo: 15 × 13 / 31.
        Assert.Equal(Dec16, next.PeriodEndUtc);
        Assert.Equal(6.29m, next.Price.Amount);
        Assert.False(next.AlignsWithoutCharge);
    }

    [Fact]
    public void A_stretch_under_fifty_cents_aligns_without_charge()
    {
        var next = SeatAlignment.ForRenewal(Fifteen, BillingCycle.Monthly, Dec3, planPeriodEndUtc: Dec3.AddDays(1));

        Assert.Equal(Dec3.AddDays(1), next.PeriodEndUtc);
        Assert.Equal(0m, next.Price.Amount);
        Assert.True(next.AlignsWithoutCharge);
    }

    [Fact]
    public void A_plan_ending_after_a_whole_seat_cycle_leaves_the_seat_on_its_own_cycle()
    {
        var next = SeatAlignment.ForRenewal(Fifteen, BillingCycle.Monthly, Dec3, planPeriodEndUtc: Dec3.AddYears(1));

        Assert.Equal(new DateTime(2027, 1, 3, 0, 0, 0, DateTimeKind.Utc), next.PeriodEndUtc);
        Assert.Equal(15m, next.Price.Amount);
    }

    [Fact]
    public void Without_a_plan_period_the_seat_keeps_its_own_cycle()
    {
        var next = SeatAlignment.ForRenewal(Fifteen, BillingCycle.Monthly, Dec3, planPeriodEndUtc: null);

        Assert.Equal(new DateTime(2027, 1, 3, 0, 0, 0, DateTimeKind.Utc), next.PeriodEndUtc);
    }

    [Fact]
    public void A_reactivation_charges_up_to_the_plan_end()
    {
        var next = SeatAlignment.ForReactivation(Fifteen, BillingCycle.Monthly, Dec3, planPeriodEndUtc: Dec16);

        Assert.Equal(Dec16, next.PeriodEndUtc);
        Assert.Equal(6.29m, next.Price.Amount);
    }

    // Unas horas antes del fin del plan no vale la pena cobrar el tramo: se regala y se paga el período siguiente.
    [Fact]
    public void A_reactivation_hours_before_the_plan_end_pays_the_next_full_period()
    {
        var next = SeatAlignment.ForReactivation(
            Fifteen,
            BillingCycle.Monthly,
            Dec16.AddHours(-12),
            planPeriodEndUtc: Dec16
        );

        Assert.Equal(new DateTime(2027, 1, 16, 0, 0, 0, DateTimeKind.Utc), next.PeriodEndUtc);
        Assert.Equal(15m, next.Price.Amount);
    }
}
