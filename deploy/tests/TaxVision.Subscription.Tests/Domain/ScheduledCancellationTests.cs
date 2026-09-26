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

    private static TenantSubscription ActiveSubscription(out DateTime periodEndUtc)
    {
        var nowUtc = DateTime.UtcNow;
        periodEndUtc = nowUtc.AddDays(30);
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("pro").Value, "pro", "pro plan", PlanTier.Standard, Guid.Empty, nowUtc)
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
                periodEndUtc,
                Guid.Empty,
                nowUtc
            )
            .Value;
    }
}
