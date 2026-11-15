using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>
/// Al cambiar de ciclo, los add-ons y asientos cambian con el plan: mismo ciclo, precio vigente del ciclo nuevo y
/// el mismo período que la base. Todo o nada: si un extra no puede cambiar, no cambia ninguno.
/// </summary>
public sealed class BillingCycleSwitchTests
{
    private static readonly DateTime Start = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddYears(1);

    [Fact]
    public void Every_active_extra_moves_to_the_new_cycle_price_and_period()
    {
        var addOn = ActiveAddOn();
        var seat = ActiveSeat();
        var prices = new Dictionary<Guid, Money> { [addOn.Id] = Usd(290m), [seat.Id] = Usd(150m) };

        var result = BillingCycleSwitch.Apply(
            BillingCycle.Yearly,
            Start,
            End,
            [addOn],
            [seat],
            prices,
            Guid.Empty,
            Start
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(BillingCycle.Yearly, addOn.BillingCycle);
        Assert.Equal(290m, addOn.UnitPrice.Amount);
        Assert.Equal((Start, End), (addOn.CurrentPeriodStartUtc, addOn.CurrentPeriodEndUtc));
        Assert.Equal(End, addOn.NextRenewalAtUtc);
        Assert.Equal(BillingCycle.Yearly, seat.BillingCycle);
        Assert.Equal(150m, seat.UnitPrice.Amount);
        Assert.Equal((Start, End), (seat.CurrentPeriodStartUtc!.Value, seat.CurrentPeriodEndUtc!.Value));
        Assert.Equal(End, seat.NextRenewalAtUtc);
    }

    // E3: un extra con cobro pendiente bloquea el cambio; el resto queda como estaba.
    [Fact]
    public void A_past_due_extra_blocks_the_switch_and_nothing_changes()
    {
        var addOn = ActiveAddOn();
        var seat = ActiveSeat();
        seat.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, Start);
        var prices = new Dictionary<Guid, Money> { [addOn.Id] = Usd(290m), [seat.Id] = Usd(150m) };

        var result = BillingCycleSwitch.Apply(
            BillingCycle.Yearly,
            Start,
            End,
            [addOn],
            [seat],
            prices,
            Guid.Empty,
            Start
        );

        Assert.True(result.IsFailure);
        Assert.Equal(BillingCycleSwitch.ExtraPastDue.Code, result.Error.Code);
        Assert.Equal(BillingCycle.Monthly, addOn.BillingCycle);
        Assert.Equal(29m, addOn.UnitPrice.Amount);
    }

    // E1: una renovación en curso de un extra bloquea el cambio.
    [Fact]
    public void A_renewal_in_progress_blocks_the_switch()
    {
        var addOn = ActiveAddOn();
        addOn.BeginRenewal("addon-renewal-x", addOn.CurrentPeriodEndUtc.AddMonths(1), Guid.Empty, Start);
        var prices = new Dictionary<Guid, Money> { [addOn.Id] = Usd(290m) };

        var result = BillingCycleSwitch.Apply(BillingCycle.Yearly, Start, End, [addOn], [], prices, Guid.Empty, Start);

        Assert.True(result.IsFailure);
        Assert.Equal(BillingCycleSwitch.RenewalInProgress.Code, result.Error.Code);
        Assert.Equal(BillingCycle.Monthly, addOn.BillingCycle);
    }

    // Un asiento comprado que todavía no se cobró (Available) es una compra en curso.
    [Fact]
    public void A_seat_purchase_in_progress_blocks_the_switch()
    {
        var seat = CreateSeat();

        var result = BillingCycleSwitch.Apply(
            BillingCycle.Yearly,
            Start,
            End,
            [],
            [seat],
            new Dictionary<Guid, Money> { [seat.Id] = Usd(150m) },
            Guid.Empty,
            Start
        );

        Assert.True(result.IsFailure);
        Assert.Equal(BillingCycleSwitch.PurchaseInProgress.Code, result.Error.Code);
    }

    // E2 lo resuelve Application al buscar precios; si aun así falta uno, no cambia nada.
    [Fact]
    public void A_missing_price_changes_nothing()
    {
        var addOn = ActiveAddOn();
        var seat = ActiveSeat();
        var prices = new Dictionary<Guid, Money> { [seat.Id] = Usd(150m) };

        var result = BillingCycleSwitch.Apply(
            BillingCycle.Yearly,
            Start,
            End,
            [addOn],
            [seat],
            prices,
            Guid.Empty,
            Start
        );

        Assert.True(result.IsFailure);
        Assert.Equal(BillingCycleSwitch.PriceMissing.Code, result.Error.Code);
        Assert.Equal(BillingCycle.Monthly, seat.BillingCycle);
    }

    // Un add-on cancelado vence al fin de su período: no entra en el cambio ni lo bloquea.
    [Fact]
    public void Cancelled_extras_are_left_out()
    {
        var cancelled = ActiveAddOn();
        cancelled.CancelActive("not needed", Guid.Empty, Start);
        var monthlyEnd = cancelled.CurrentPeriodEndUtc;

        var result = BillingCycleSwitch.Apply(
            BillingCycle.Yearly,
            Start,
            End,
            [cancelled],
            [],
            new Dictionary<Guid, Money>(),
            Guid.Empty,
            Start
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(BillingCycle.Monthly, cancelled.BillingCycle);
        Assert.Equal(monthlyEnd, cancelled.CurrentPeriodEndUtc);
    }

    [Fact]
    public void The_extras_that_switch_are_the_active_ones()
    {
        var active = ActiveAddOn();
        var cancelled = ActiveAddOn();
        cancelled.CancelActive("not needed", Guid.Empty, Start);
        var seat = ActiveSeat();

        var included = BillingCycleSwitch.IncludedExtras([active, cancelled], [seat]);

        Assert.Equal([active.Id], included.AddOns.Select(a => a.Id));
        Assert.Equal([seat.Id], included.Seats.Select(s => s.Id));
    }

    private static Money Usd(decimal amount) => Money.Create(amount, "USD").Value;

    private static TenantAddOn ActiveAddOn()
    {
        var definition = AddOnDefinition
            .Create(
                AddOnCode.Create("module-addon-email").Value,
                "Email",
                "desc",
                "module",
                allowMultipleInstances: false,
                [BillingCycle.Monthly, BillingCycle.Yearly],
                Guid.Empty,
                Start
            )
            .Value;

        return TenantAddOn
            .Purchase(
                Guid.NewGuid(),
                definition,
                1,
                Usd(29m),
                BillingCycle.Monthly,
                true,
                Guid.Empty,
                Start.AddDays(-10)
            )
            .Value;
    }

    private static SubscriptionSeat CreateSeat() =>
        SubscriptionSeat
            .Purchase(
                Guid.NewGuid(),
                SeatType.Standard,
                SeatSourceType.Plan,
                sourceReferenceId: null,
                Usd(15m),
                BillingCycle.Monthly,
                autoRenew: true,
                Guid.Empty,
                Start.AddDays(-10)
            )
            .Value;

    private static SubscriptionSeat ActiveSeat()
    {
        var seat = CreateSeat();
        seat.Activate(Start.AddDays(-10), Start.AddDays(20), Guid.Empty, Start.AddDays(-10));
        return seat;
    }
}
