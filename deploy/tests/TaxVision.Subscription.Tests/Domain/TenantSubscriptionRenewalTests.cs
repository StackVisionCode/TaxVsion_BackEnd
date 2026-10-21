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
    public void FailRenewal_with_a_permanent_failure_code_moves_to_past_due_even_when_caller_reports_willRetry_true()
    {
        // Regression: PaymentApp can report WillRetry=true forever for charges that fail
        // before reaching Processing (e.g. no payment method saved), so the subscription
        // must not blindly trust it for known-permanent failure codes.
        var subscription = CreateActiveSubscription();
        subscription.BeginRenewal("key-1", Guid.Empty, DateTime.UtcNow);
        var renewalId = subscription.Renewals.First().Id;

        var result = subscription.FailRenewal(
            renewalId,
            "requires_payment_method",
            "No payment method on file",
            willRetry: true,
            DateTime.UtcNow.AddHours(1),
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        Assert.Equal(RenewalStatus.Failed, subscription.Renewals.First().Status);
    }

    [Fact]
    public void FailRenewal_forces_past_due_once_retry_attempts_are_exhausted_even_when_caller_reports_willRetry_true()
    {
        // Regression: same upstream WillRetry-always-true bug, but for failure codes that
        // aren't in the known-permanent list — the subscription's own retry cap must still
        // kick in instead of staying Active indefinitely.
        var subscription = CreateActiveSubscription();
        subscription.BeginRenewal("key-1", Guid.Empty, DateTime.UtcNow);
        var renewalId = subscription.Renewals.First().Id;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var retryResult = subscription.FailRenewal(
                renewalId,
                "temporary_failure",
                "Try again later",
                willRetry: true,
                DateTime.UtcNow.AddHours(1),
                Guid.Empty,
                DateTime.UtcNow
            );
            Assert.True(retryResult.IsSuccess);
            Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        }

        var finalResult = subscription.FailRenewal(
            renewalId,
            "temporary_failure",
            "Try again later",
            willRetry: true,
            DateTime.UtcNow.AddHours(1),
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(finalResult.IsSuccess);
        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        Assert.Equal(RenewalStatus.Failed, subscription.Renewals.First().Status);
    }

    [Fact]
    public void CompleteRenewal_for_an_unknown_renewal_fails()
    {
        var subscription = CreateActiveSubscription();

        var result = subscription.CompleteRenewal(Guid.NewGuid(), null, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.RenewalNotFound", result.Error.Code);
    }

    [Fact]
    public void Paying_by_hand_cancels_the_automatic_renewal_still_in_retry_and_reports_its_key()
    {
        var subscription = CreateActiveSubscription();
        subscription.BeginRenewal("auto-key", Guid.Empty, DateTime.UtcNow);
        var auto = subscription.Renewals.First();
        subscription.FailRenewal(
            auto.Id,
            "card_declined",
            "Declined",
            true,
            DateTime.UtcNow.AddHours(1),
            Guid.Empty,
            DateTime.UtcNow
        );

        var keys = subscription.SettleUnpaidRenewals("manual-key", Guid.Empty, DateTime.UtcNow);

        Assert.Equal(["auto-key"], keys);
        Assert.Equal(RenewalStatus.Cancelled, auto.Status);
        Assert.Null(auto.NextRetryAtUtc);
    }

    [Fact]
    public void Paying_by_hand_reports_failed_renewals_but_not_paid_ones_or_its_own()
    {
        var subscription = CreateActiveSubscription();
        subscription.BeginRenewal("failed-key", Guid.Empty, DateTime.UtcNow);
        var failed = subscription.Renewals.First();
        subscription.FailRenewal(failed.Id, "card_declined", "Declined", false, null, Guid.Empty, DateTime.UtcNow);
        subscription.ReactivateAfterSelfServicePayment(
            DateTime.UtcNow,
            DateTime.UtcNow.AddMonths(1),
            Guid.Empty,
            DateTime.UtcNow
        );
        subscription.BeginRenewal("manual-key", Guid.Empty, DateTime.UtcNow);
        var manual = subscription.Renewals.Last();
        subscription.CompleteRenewal(manual.Id, "pi_manual", Guid.Empty, DateTime.UtcNow);

        var keys = subscription.SettleUnpaidRenewals("manual-key", Guid.Empty, DateTime.UtcNow);

        Assert.Equal(["failed-key"], keys);
        Assert.Equal(RenewalStatus.Failed, failed.Status);
        Assert.Equal(RenewalStatus.Succeeded, manual.Status);
    }

    [Fact]
    public void A_cancelled_renewal_can_not_advance_the_period_again()
    {
        var subscription = CreateActiveSubscription();
        subscription.BeginRenewal("auto-key", Guid.Empty, DateTime.UtcNow);
        var auto = subscription.Renewals.First();
        subscription.SettleUnpaidRenewals("manual-key", Guid.Empty, DateTime.UtcNow);
        var periodEnd = subscription.CurrentPeriodEndUtc;

        var late = subscription.CompleteRenewal(auto.Id, "pi_late", Guid.Empty, DateTime.UtcNow);

        Assert.True(late.IsFailure);
        Assert.Equal(periodEnd, subscription.CurrentPeriodEndUtc);
    }

    [Fact]
    public void The_activation_charge_holds_the_renewal_key_of_its_own_period()
    {
        var subscription = CreateActiveSubscription();
        var legacyKey = $"subscription-renewal-{subscription.Id:N}-{subscription.CurrentPeriodEndUtc:yyyyMMdd}";
        subscription.BeginActivationCharge(legacyKey, Guid.Empty, DateTime.UtcNow);

        Assert.True(subscription.IsRenewalKeyUsedByAnotherPeriod(legacyKey));
        Assert.False(subscription.IsRenewalKeyUsedByAnotherPeriod("unused-key"));
    }

    [Fact]
    public void A_period_that_ended_days_ago_without_renewing_suspends_and_stops_its_charges()
    {
        var subscription = CreateActiveSubscription();
        subscription.BeginRenewal("stuck-key", Guid.Empty, DateTime.UtcNow);
        var nowUtc = subscription.CurrentPeriodEndUtc.AddDays(3);

        var result = subscription.SuspendBecauseRenewalOverdue(TimeSpan.FromDays(2), Guid.Empty, nowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
        Assert.Equal("RenewalOverdue", subscription.SuspensionReason);
        Assert.Equal(["stuck-key"], result.Value);
        Assert.Equal(RenewalStatus.Cancelled, subscription.Renewals.Single().Status);
    }

    [Fact]
    public void Within_the_tolerance_the_subscription_stays_active()
    {
        var subscription = CreateActiveSubscription();
        var nowUtc = subscription.CurrentPeriodEndUtc.AddDays(1);

        var result = subscription.SuspendBecauseRenewalOverdue(TimeSpan.FromDays(2), Guid.Empty, nowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public void A_renewal_still_being_retried_is_not_overdue()
    {
        var subscription = CreateActiveSubscription();
        var periodEnd = subscription.CurrentPeriodEndUtc;
        subscription.BeginRenewal("retry-key", Guid.Empty, DateTime.UtcNow);
        subscription.FailRenewal(
            subscription.Renewals.Single().Id,
            "card_declined",
            "Declined",
            true,
            periodEnd.AddDays(2.5),
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.False(subscription.IsRenewalOverdue(TimeSpan.FromDays(2), periodEnd.AddDays(3)));
    }

    private static TenantSubscription CreateActiveSubscription()
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("starter").Value, "Starter", "desc", PlanTier.Starter, Guid.Empty, DateTime.UtcNow)
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
