using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>
/// Los extras siguen el ciclo de la base. Mientras un cambio espera su cobro o hay un paso a otro ciclo agendado,
/// sus renovaciones esperan: si no, se cobrarían con el ciclo viejo. Al pasar a mensual, cada extra toma el precio
/// mensual vigente para su próxima renovación, sin mover el período que ya pagó.
/// </summary>
public sealed class ExtraRenewalHoldTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void Extras_do_not_renew_while_a_change_awaits_its_payment()
    {
        var (subscription, plan, version) = Subscription(BillingCycle.Monthly);
        subscription.RequestUpgrade(
            plan,
            version,
            BillingCycle.Yearly,
            99_900,
            "USD",
            "plan-change-hold",
            Guid.Empty,
            Now
        );

        Assert.True(subscription.HoldsExtraRenewals(subscription.CurrentPeriodEndUtc));
    }

    [Fact]
    public void Extras_ending_with_the_base_wait_for_a_scheduled_switch_to_monthly()
    {
        var (subscription, plan, version) = Subscription(BillingCycle.Yearly);
        subscription.RequestDowngrade(plan, version, BillingCycle.Monthly, Guid.Empty, Now);

        Assert.True(subscription.HoldsExtraRenewals(subscription.CurrentPeriodEndUtc));
        Assert.False(subscription.HoldsExtraRenewals(subscription.CurrentPeriodEndUtc.AddDays(-30)));
    }

    [Fact]
    public void With_nothing_pending_extras_renew_as_usual()
    {
        var (subscription, _, _) = Subscription(BillingCycle.Monthly);

        Assert.False(subscription.HoldsExtraRenewals(subscription.CurrentPeriodEndUtc));
    }

    [Fact]
    public void Repricing_for_the_next_renewal_keeps_the_paid_period()
    {
        var seat = SubscriptionSeat
            .Purchase(
                Guid.NewGuid(),
                SeatType.Standard,
                SeatSourceType.Plan,
                null,
                Usd(150m),
                BillingCycle.Yearly,
                true,
                Guid.Empty,
                Now
            )
            .Value;
        seat.Activate(Now, Now.AddYears(1), Guid.Empty, Now);

        var result = seat.RepriceForNextRenewal(BillingCycle.Monthly, Usd(15m), Guid.Empty, Now);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal((BillingCycle.Monthly, 15m), (seat.BillingCycle, seat.UnitPrice.Amount));
        Assert.Equal(Now.AddYears(1), seat.CurrentPeriodEndUtc);
    }

    [Fact]
    public void An_add_on_is_repriced_for_its_next_renewal_too()
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
                Now
            )
            .Value;
        var addOn = TenantAddOn
            .Purchase(Guid.NewGuid(), definition, 1, Usd(290m), BillingCycle.Yearly, true, Guid.Empty, Now)
            .Value;
        var paidEnd = addOn.CurrentPeriodEndUtc;

        var result = addOn.RepriceForNextRenewal(BillingCycle.Monthly, Usd(29m), Guid.Empty, Now);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal((BillingCycle.Monthly, 29m), (addOn.BillingCycle, addOn.UnitPrice.Amount));
        Assert.Equal(paidEnd, addOn.CurrentPeriodEndUtc);
    }

    private static Money Usd(decimal amount) => Money.Create(amount, "USD").Value;

    private static (TenantSubscription, SubscriptionPlan, SubscriptionPlanVersion) Subscription(BillingCycle cycle)
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("enterprise").Value, "Enterprise", "plan", PlanTier.Starter, Guid.Empty, Now)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, 1, trialDaysDefault: 14, [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;
        plan.AddVersion(version, Guid.Empty, Now);
        plan.PublishVersion(version.Id, Now, Guid.Empty, Now);
        var end = cycle == BillingCycle.Yearly ? Now.AddYears(1) : Now.AddDays(30);
        var subscription = TenantSubscription
            .ActivateImmediately(Guid.NewGuid(), plan, version, cycle, Now, end, Guid.Empty, Now)
            .Value;
        return (subscription, plan, version);
    }
}
