using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// El aviso de renovación: el anual avisa a 30, 7 y 1 días (es un cobro grande); el mensual a 7 y 1, para no recibir el
/// de 30 recién renovado. Muestra el total que se va a cobrar (E4): plan más add-ons y asientos activos, cada uno a su
/// precio contratado.
/// </summary>
public sealed class RenewalNoticeTests
{
    [Theory]
    [InlineData(BillingCycle.Yearly, 30, true)]
    [InlineData(BillingCycle.Yearly, 7, true)]
    [InlineData(BillingCycle.Yearly, 1, true)]
    [InlineData(BillingCycle.Yearly, 14, false)]
    [InlineData(BillingCycle.Monthly, 30, false)]
    [InlineData(BillingCycle.Monthly, 7, true)]
    [InlineData(BillingCycle.Monthly, 1, true)]
    public void Who_is_notified_and_when(BillingCycle cycle, int daysUntilRenewal, bool notified) =>
        Assert.Equal(notified, RenewalNotice.ShouldNotify(cycle, daysUntilRenewal));

    [Fact]
    public void The_notice_window_covers_the_yearly_30_days() => Assert.Equal(31, RenewalNotice.WindowDays);

    [Fact]
    public void The_amount_is_the_plan_plus_every_active_extra_at_its_contracted_price()
    {
        var now = DateTime.UtcNow;
        var definition = AddOnDefinition
            .Create(
                AddOnCode.Create("module-addon-email").Value,
                "Email",
                "desc",
                "module",
                allowMultipleInstances: true,
                [BillingCycle.Yearly],
                Guid.Empty,
                now
            )
            .Value;
        var active = TenantAddOn
            .Purchase(Guid.NewGuid(), definition, 2, Usd(290m), BillingCycle.Yearly, true, Guid.Empty, now)
            .Value;
        var cancelled = TenantAddOn
            .Purchase(Guid.NewGuid(), definition, 1, Usd(290m), BillingCycle.Yearly, true, Guid.Empty, now)
            .Value;
        cancelled.CancelActive("not needed", Guid.Empty, now);
        var seat = SubscriptionSeat
            .Purchase(
                Guid.NewGuid(),
                SeatType.Standard,
                SeatSourceType.Plan,
                null,
                Usd(100m),
                BillingCycle.Yearly,
                true,
                Guid.Empty,
                now
            )
            .Value;
        seat.Activate(now, now.AddYears(1), Guid.Empty, now);

        var total = RenewalNotice.TotalCents(99_900, [active, cancelled], [seat]);

        Assert.Equal(99_900 + 2 * 29_000 + 10_000, total);
    }

    private static Money Usd(decimal amount) => Money.Create(amount, "USD").Value;
}
