using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>
/// C4 — cancelar ya no apaga nada al instante. El período está pagado, así que la suscripción sigue Active
/// (y por tanto dando acceso) hasta su fin, y recién ahí expira. Deshacerlo no cuesta nada.
/// </summary>
public sealed class ScheduledCancellationTests
{
    // Aceptación de la fase: cancelar hoy mantiene el acceso hasta el fin del período.
    [Fact]
    public void Scheduling_a_cancellation_keeps_the_subscription_active_until_the_period_ends()
    {
        var subscription = ActiveSubscription(out var periodEndUtc);

        var result = subscription.ScheduleCancellation("too expensive", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.True(SubscriptionAccess.GrantsAccess(subscription.Status));
        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.Equal(periodEndUtc, subscription.CurrentPeriodEndUtc);
        Assert.Null(subscription.ExpiredAtUtc);
    }

    [Fact]
    public void Scheduling_twice_changes_nothing()
    {
        var subscription = ActiveSubscription(out _);
        subscription.ScheduleCancellation("too expensive", Guid.Empty, DateTime.UtcNow);
        var scheduledAt = subscription.CancellationScheduledAtUtc;

        var again = subscription.ScheduleCancellation("changed my mind again", Guid.Empty, DateTime.UtcNow.AddDays(1));

        Assert.True(again.IsSuccess);
        Assert.Equal(scheduledAt, subscription.CancellationScheduledAtUtc);
        Assert.Equal("too expensive", subscription.CancellationReason);
    }

    // Aceptación de la fase: "Resume" lo revierte sin cobro.
    [Fact]
    public void Resuming_clears_the_scheduled_cancellation()
    {
        var subscription = ActiveSubscription(out _);
        subscription.ScheduleCancellation("too expensive", Guid.Empty, DateTime.UtcNow);

        var result = subscription.ResumeCancellation(Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.False(subscription.CancelAtPeriodEnd);
        Assert.Null(subscription.CancellationScheduledAtUtc);
        Assert.Null(subscription.CancellationReason);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public void Resuming_without_a_scheduled_cancellation_fails()
    {
        var subscription = ActiveSubscription(out _);

        var result = subscription.ResumeCancellation(Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.NoScheduledCancellation", result.Error.Code);
    }

    [Fact]
    public void The_period_end_expires_a_scheduled_cancellation()
    {
        var subscription = ActiveSubscription(out var periodEndUtc);
        subscription.ScheduleCancellation("too expensive", Guid.Empty, DateTime.UtcNow);

        var result = subscription.ExpireAfterScheduledCancellation(Guid.Empty, periodEndUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Expired, subscription.Status);
        Assert.False(SubscriptionAccess.GrantsAccess(subscription.Status));
        Assert.NotNull(subscription.CancelledAtUtc);
        Assert.NotNull(subscription.ExpiredAtUtc);
    }

    [Fact]
    public void Without_a_scheduled_cancellation_nothing_expires()
    {
        var subscription = ActiveSubscription(out var periodEndUtc);

        var result = subscription.ExpireAfterScheduledCancellation(Guid.Empty, periodEndUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.NoScheduledCancellation", result.Error.Code);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public void Changing_plan_after_scheduling_cancellation_clears_the_cancellation()
    {
        var subscription = ActiveSubscription(out _, out var plan, out var version);
        subscription.ScheduleCancellation("too expensive", Guid.Empty, DateTime.UtcNow);
        Assert.True(subscription.CancelAtPeriodEnd);

        var changed = subscription.ChangePlan(plan, version, BillingCycle.Yearly, Guid.Empty, DateTime.UtcNow);

        Assert.True(changed.IsSuccess);
        Assert.False(subscription.CancelAtPeriodEnd);
        Assert.Null(subscription.CancellationScheduledAtUtc);
        Assert.Null(subscription.CancellationReason);
    }

    [Fact]
    public void Requesting_an_upgrade_after_scheduling_cancellation_clears_the_cancellation()
    {
        var subscription = ActiveSubscription(out _, out var plan, out var version);
        subscription.ScheduleCancellation("too expensive", Guid.Empty, DateTime.UtcNow);
        Assert.True(subscription.CancelAtPeriodEnd);

        var upgraded = subscription.RequestUpgrade(
            plan,
            version,
            BillingCycle.Yearly,
            chargeAmountCents: 10000,
            chargeCurrency: "USD",
            paymentIdempotencyKey: Guid.NewGuid().ToString("N"),
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(upgraded.IsSuccess);
        Assert.False(subscription.CancelAtPeriodEnd);
        Assert.Null(subscription.CancellationScheduledAtUtc);
        Assert.Null(subscription.CancellationReason);
    }

    // SUB-15: el upgrade no se pagó, así que lo agendado sigue en pie.
    [Theory]
    [InlineData("no-session")]
    [InlineData("cancelled")]
    [InlineData("payment-failed")]
    public void An_upgrade_that_does_not_go_through_keeps_the_scheduled_cancellation(string outcome)
    {
        var subscription = ActiveSubscription(out _, out var plan, out var version);
        subscription.ScheduleCancellation("too expensive", Guid.Empty, DateTime.UtcNow);
        var scheduledAt = subscription.CancellationScheduledAtUtc;

        var request = RequestHostedUpgrade(subscription, plan, version);
        Close(subscription, request, outcome);

        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.Equal(scheduledAt, subscription.CancellationScheduledAtUtc);
        Assert.Equal("too expensive", subscription.CancellationReason);
    }

    [Theory]
    [InlineData("no-session")]
    [InlineData("cancelled")]
    [InlineData("payment-failed")]
    public void An_upgrade_that_does_not_go_through_keeps_the_scheduled_downgrade(string outcome)
    {
        var subscription = ActiveSubscription(out _, out var plan, out var version);
        subscription.RequestDowngrade(plan, version, BillingCycle.Yearly, Guid.Empty, DateTime.UtcNow);
        var downgrade = Assert.Single(subscription.PendingDowngrades);

        var request = RequestHostedUpgrade(subscription, plan, version);
        Assert.Equal(PendingDowngradeStatus.Cancelled, downgrade.Status);
        Close(subscription, request, outcome);

        Assert.Equal(PendingDowngradeStatus.Scheduled, downgrade.Status);
        Assert.Null(downgrade.CancelledAtUtc);
    }

    [Fact]
    public void A_paid_upgrade_does_not_bring_back_what_it_replaced()
    {
        var subscription = ActiveSubscription(out _, out var plan, out var version);
        subscription.ScheduleCancellation("too expensive", Guid.Empty, DateTime.UtcNow);

        var request = RequestHostedUpgrade(subscription, plan, version);
        subscription.CompleteUpgradeCharge(request.Id, plan, version, Guid.NewGuid(), Guid.Empty, DateTime.UtcNow);

        Assert.Equal(PlanChangeRequestStatus.Applied, request.Status);
        Assert.False(subscription.CancelAtPeriodEnd);
    }

    [Fact]
    public void A_subscription_that_already_ended_does_not_get_its_schedule_back()
    {
        var subscription = ActiveSubscription(out _, out var plan, out var version);
        subscription.ScheduleCancellation("too expensive", Guid.Empty, DateTime.UtcNow);
        var request = RequestHostedUpgrade(subscription, plan, version);
        subscription.CancelImmediately("support", Guid.Empty, DateTime.UtcNow);

        subscription.ExpireUpgradeWithoutCheckout(request.Id, Guid.Empty, DateTime.UtcNow);

        Assert.Equal(PlanChangeRequestStatus.Expired, request.Status);
        Assert.False(subscription.CancelAtPeriodEnd);
    }

    private static PlanChangeRequest RequestHostedUpgrade(
        TenantSubscription subscription,
        SubscriptionPlan plan,
        SubscriptionPlanVersion version
    )
    {
        var upgraded = subscription.RequestUpgrade(
            plan,
            version,
            BillingCycle.Yearly,
            chargeAmountCents: 10000,
            chargeCurrency: "USD",
            paymentIdempotencyKey: Guid.NewGuid().ToString("N"),
            Guid.Empty,
            DateTime.UtcNow,
            usesHostedCheckout: true
        );
        Assert.True(upgraded.IsSuccess);
        return Assert.Single(subscription.PlanChangeRequests);
    }

    private static void Close(TenantSubscription subscription, PlanChangeRequest request, string outcome)
    {
        var nowUtc = DateTime.UtcNow;
        var closed = outcome switch
        {
            "no-session" => subscription.ExpireUpgradeWithoutCheckout(request.Id, Guid.Empty, nowUtc),
            "cancelled" => (Result)subscription.CancelPendingUpgrade(Guid.Empty, nowUtc, "changed my mind"),
            _ => subscription.FailUpgradeCharge(request.Id, Guid.NewGuid(), nowUtc),
        };
        Assert.True(closed.IsSuccess);
        Assert.NotEqual(PlanChangeRequestStatus.AwaitingPayment, request.Status);
    }

    private static TenantSubscription ActiveSubscription(out DateTime periodEndUtc)
    {
        return ActiveSubscription(out periodEndUtc, out _, out _);
    }

    private static TenantSubscription ActiveSubscription(
        out DateTime periodEndUtc,
        out SubscriptionPlan plan,
        out SubscriptionPlanVersion version
    )
    {
        var nowUtc = DateTime.UtcNow;
        periodEndUtc = nowUtc.AddDays(30);
        plan = SubscriptionPlan
            .Create(PlanCode.Create("pro").Value, "pro", "pro plan", PlanTier.Starter, Guid.Empty, nowUtc)
            .Value;
        version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly, BillingCycle.Yearly]).Value;
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);

        return TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                nowUtc,
                periodEndUtc,
                Guid.Empty,
                nowUtc
            )
            .Value;
    }
}
