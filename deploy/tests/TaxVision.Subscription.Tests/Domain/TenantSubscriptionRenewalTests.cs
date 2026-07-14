using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Renewals;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class TenantSubscriptionRenewalTests
{
    [Fact]
    public void BeginRenewal_schedules_a_renewal_for_an_active_subscription()
    {
        var subscription = CreateActiveSubscription();

        var result = subscription.BeginRenewal("key-1", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Single(subscription.Renewals);
        Assert.Equal(RenewalStatus.Scheduled, subscription.Renewals.First().Status);
    }

    [Fact]
    public void BeginRenewal_with_the_same_idempotency_key_does_not_create_a_duplicate()
    {
        var subscription = CreateActiveSubscription();
        subscription.BeginRenewal("key-1", Guid.Empty, DateTime.UtcNow);

        var result = subscription.BeginRenewal("key-1", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Single(subscription.Renewals);
    }

    [Fact]
    public void CompleteRenewal_advances_the_current_period()
    {
        var subscription = CreateActiveSubscription();
        var originalPeriodEnd = subscription.CurrentPeriodEndUtc;
        subscription.BeginRenewal("key-1", Guid.Empty, DateTime.UtcNow);
        var renewalId = subscription.Renewals.First().Id;

        var result = subscription.CompleteRenewal(renewalId, "ext-ref-123", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(originalPeriodEnd, subscription.CurrentPeriodStartUtc);
        Assert.True(subscription.CurrentPeriodEndUtc > originalPeriodEnd);
        Assert.Equal(RenewalStatus.Succeeded, subscription.Renewals.First().Status);
    }

    [Fact]
    public void FailRenewal_without_retry_moves_the_subscription_to_past_due()
    {
        var subscription = CreateActiveSubscription();
        subscription.BeginRenewal("key-1", Guid.Empty, DateTime.UtcNow);
        var renewalId = subscription.Renewals.First().Id;

        var result = subscription.FailRenewal(
            renewalId,
            "card_declined",
            "Card declined",
            willRetry: false,
            null,
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        Assert.Equal(RenewalStatus.Failed, subscription.Renewals.First().Status);
    }

    [Fact]
    public void FailRenewal_with_retry_keeps_the_subscription_active_and_schedules_a_retry()
    {
        var subscription = CreateActiveSubscription();
        subscription.BeginRenewal("key-1", Guid.Empty, DateTime.UtcNow);
        var renewalId = subscription.Renewals.First().Id;
        var nextRetry = DateTime.UtcNow.AddHours(6);

        var result = subscription.FailRenewal(
            renewalId,
            "temporary_failure",
            "Try again later",
            willRetry: true,
            nextRetry,
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(RenewalStatus.RetryScheduled, subscription.Renewals.First().Status);
        Assert.Equal(1, subscription.Renewals.First().RetryCount);
    }

    [Fact]
    public void CompleteRenewal_for_an_unknown_renewal_fails()
    {
        var subscription = CreateActiveSubscription();

        var result = subscription.CompleteRenewal(Guid.NewGuid(), null, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.RenewalNotFound", result.Error.Code);
    }

    private static TenantSubscription CreateActiveSubscription()
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("starter").Value, "Starter", "desc", PlanTier.Standard, Guid.Empty, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        var nowUtc = DateTime.UtcNow;
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
