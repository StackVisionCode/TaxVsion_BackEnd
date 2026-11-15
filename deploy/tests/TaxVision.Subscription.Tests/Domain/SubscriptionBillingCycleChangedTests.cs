using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Application.Subscriptions.DomainEvents;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.Subscriptions.Events;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>
/// Cambiar de ciclo avisa al cliente: pasar a anual, cuando se paga (cuánto se cobró y cuándo renueva); pasar a
/// mensual, cuando se agenda (desde cuándo). Viaja por el canal de estado con el motivo BillingCycleChanged.
/// </summary>
public sealed class SubscriptionBillingCycleChangedTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Paying_the_switch_to_yearly_announces_the_charge_and_the_next_renewal()
    {
        var (subscription, plan, version) = Subscription(BillingCycle.Monthly);
        subscription.RequestUpgrade(
            plan,
            version,
            BillingCycle.Yearly,
            158_900,
            "USD",
            "plan-change-cycle",
            Guid.Empty,
            Now
        );
        var request = Assert.Single(subscription.PlanChangeRequests);
        subscription.ClearDomainEvents();

        subscription.CompleteUpgradeCharge(request.Id, plan, version, Guid.NewGuid(), Guid.Empty, Now);

        var changed = Assert.Single(subscription.DomainEvents.OfType<SubscriptionBillingCycleChangedDomainEvent>());
        Assert.Equal(BillingCycle.Yearly, changed.NewBillingCycle);
        Assert.Equal(158_900, changed.ChargedAmountCents);
        Assert.Equal(Now, changed.EffectiveAtUtc);
        Assert.Equal(subscription.CurrentPeriodEndUtc, changed.NextRenewalAtUtc);
    }

    [Fact]
    public void Scheduling_the_switch_to_monthly_announces_when_it_starts()
    {
        var (subscription, plan, version) = Subscription(BillingCycle.Yearly);
        subscription.ClearDomainEvents();

        subscription.RequestDowngrade(plan, version, BillingCycle.Monthly, Guid.Empty, Now);

        var changed = Assert.Single(subscription.DomainEvents.OfType<SubscriptionBillingCycleChangedDomainEvent>());
        Assert.Equal(BillingCycle.Monthly, changed.NewBillingCycle);
        Assert.Null(changed.ChargedAmountCents);
        Assert.Equal(subscription.CurrentPeriodEndUtc, changed.EffectiveAtUtc);
    }

    [Fact]
    public void A_plan_change_that_keeps_the_cycle_does_not_announce_a_cycle_change()
    {
        var (subscription, plan, version) = Subscription(BillingCycle.Monthly);
        subscription.RequestUpgrade(
            plan,
            version,
            BillingCycle.Monthly,
            9_900,
            "USD",
            "plan-change-same-cycle",
            Guid.Empty,
            Now
        );
        var request = Assert.Single(subscription.PlanChangeRequests);
        subscription.ClearDomainEvents();

        subscription.CompleteUpgradeCharge(request.Id, plan, version, Guid.NewGuid(), Guid.Empty, Now);

        Assert.Empty(subscription.DomainEvents.OfType<SubscriptionBillingCycleChangedDomainEvent>());
    }

    [Fact]
    public void The_notice_travels_with_amount_cadence_and_dates()
    {
        var changed = new SubscriptionBillingCycleChangedDomainEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Enterprise",
            BillingCycle.Yearly,
            158_900,
            "USD",
            Now,
            Now.AddYears(1),
            SubscriptionStatus.Active,
            Guid.Empty,
            Now
        );

        var notice = SubscriptionBillingCycleChangedDomainEventHandler.Handle(changed, new Envelope());

        Assert.Equal(nameof(SubscriptionChangeReason.BillingCycleChanged), notice.Reason);
        Assert.Equal("1589.00", notice.Amount);
        Assert.Equal("year", notice.Cadence);
        Assert.Equal("November 12, 2026", notice.DateCharged);
        Assert.Equal("November 12, 2027", notice.NextChargeDate);
        Assert.Equal(notice.Status, notice.PreviousStatus);
    }

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
