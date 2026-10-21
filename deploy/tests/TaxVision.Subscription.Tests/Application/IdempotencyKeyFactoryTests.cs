using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>La primera renovación tras activar desde el trial no puede reusar la clave del cobro de activación.</summary>
public sealed class IdempotencyKeyFactoryTests
{
    [Fact]
    public void Activation_and_renewal_keys_never_collide()
    {
        var id = Guid.NewGuid();
        var periodEnd = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

        Assert.NotEqual(
            IdempotencyKeyFactory.SubscriptionActivation(id, periodEnd),
            IdempotencyKeyFactory.SubscriptionRenewal(id, periodEnd)
        );
    }

    [Fact]
    public void Renewal_uses_the_normal_key_when_it_is_free()
    {
        var subscription = ActiveSubscription();

        Assert.Equal(
            IdempotencyKeyFactory.SubscriptionRenewal(subscription.Id, subscription.CurrentPeriodEndUtc),
            IdempotencyKeyFactory.SubscriptionRenewalFor(subscription)
        );
    }

    [Fact]
    public void Renewal_after_an_old_activation_gets_its_own_key()
    {
        var subscription = ActiveSubscription();
        // Así activaba el código viejo: con la clave de la renovación del mismo fin de período.
        var legacyActivationKey = IdempotencyKeyFactory.SubscriptionRenewal(
            subscription.Id,
            subscription.CurrentPeriodEndUtc
        );
        subscription.BeginActivationCharge(legacyActivationKey, Guid.Empty, DateTime.UtcNow);

        var renewalKey = IdempotencyKeyFactory.SubscriptionRenewalFor(subscription);

        Assert.NotEqual(legacyActivationKey, renewalKey);
        Assert.True(subscription.BeginRenewal(renewalKey, Guid.Empty, DateTime.UtcNow).IsSuccess);
        Assert.Equal(2, subscription.Renewals.Count);
    }

    private static TenantSubscription ActiveSubscription()
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("starter").Value, "Starter", "desc", PlanTier.Starter, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);
        return TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                nowUtc,
                nowUtc.AddMonths(1),
                Guid.Empty,
                nowUtc
            )
            .Value;
    }
}
