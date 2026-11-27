using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>
/// Una oficina suspendida o cerrada por la plataforma no se cobra: la suscripción retiene el cobro
/// (base, asientos y add-ons) hasta que la oficina vuelve a estar activa. No cambia el estado comercial.
/// </summary>
public sealed class AdministrativeBillingHoldTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void A_suspended_office_holds_its_billing_and_its_extras()
    {
        var subscription = ActiveSubscription();

        var changed = subscription.ApplyTenantStatus(tenantIsActive: false, tenantVersion: 3, Now);

        Assert.True(changed);
        Assert.True(subscription.IsBillingHeld);
        Assert.True(subscription.HoldsExtraRenewals(subscription.CurrentPeriodEndUtc));
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public void Reactivating_the_office_releases_the_hold()
    {
        var subscription = ActiveSubscription();
        subscription.ApplyTenantStatus(tenantIsActive: false, tenantVersion: 3, Now);

        var changed = subscription.ApplyTenantStatus(tenantIsActive: true, tenantVersion: 4, Now.AddDays(5));

        Assert.True(changed);
        Assert.False(subscription.IsBillingHeld);
        Assert.Equal(Now.AddDays(5), subscription.BillingHoldReleasedAtUtc);
    }

    [Fact]
    public void An_older_status_message_does_not_undo_a_newer_one()
    {
        var subscription = ActiveSubscription();
        subscription.ApplyTenantStatus(tenantIsActive: false, tenantVersion: 5, Now);

        var changed = subscription.ApplyTenantStatus(tenantIsActive: true, tenantVersion: 4, Now);

        Assert.False(changed);
        Assert.True(subscription.IsBillingHeld);
    }

    [Fact]
    public void Repeating_the_same_status_changes_nothing()
    {
        var subscription = ActiveSubscription();
        subscription.ApplyTenantStatus(tenantIsActive: false, tenantVersion: 3, Now);

        Assert.False(subscription.ApplyTenantStatus(tenantIsActive: false, tenantVersion: 3, Now));
        Assert.False(ActiveSubscription().ApplyTenantStatus(tenantIsActive: true, tenantVersion: 1, Now));
    }

    private static TenantSubscription ActiveSubscription()
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("enterprise").Value, "Enterprise", "plan", PlanTier.Starter, Guid.Empty, Now)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, 1, trialDaysDefault: 14, [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;
        plan.AddVersion(version, Guid.Empty, Now);
        plan.PublishVersion(version.Id, Now, Guid.Empty, Now);
        return TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                Now,
                Now.AddDays(30),
                Guid.Empty,
                Now
            )
            .Value;
    }
}
