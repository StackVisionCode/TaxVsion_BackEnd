using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>
/// Renovación/reactivación self-service (plan de Expiración/Dunning, Fase 4): el dueño paga y la
/// suscripción vuelve a Active con período nuevo desde cualquier estado de lapso. A diferencia de
/// <c>ReactivateAfterAdminReview</c> (solo Suspended), ésta acepta también PastDue/GracePeriod/Expired.
/// </summary>
public sealed class TenantSubscriptionSelfServiceRenewalTests
{
    [Fact]
    public void Reactivates_from_Expired_to_Active_with_new_period_and_cleared_lapse_marks()
    {
        var subscription = DrivenToExpired();
        var newStart = DateTime.UtcNow;
        var newEnd = newStart.AddMonths(1);

        var result = subscription.ReactivateAfterSelfServicePayment(newStart, newEnd, Guid.NewGuid(), newStart);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Null(subscription.ExpiredAtUtc);
        Assert.Null(subscription.SuspendedAtUtc);
        Assert.Null(subscription.SuspensionReason);
        Assert.Null(subscription.GracePeriodEndsAtUtc);
        Assert.Equal(newStart, subscription.CurrentPeriodStartUtc);
        Assert.Equal(newEnd, subscription.CurrentPeriodEndUtc);
        Assert.Equal(newEnd, subscription.NextRenewalAtUtc);
    }

    [Theory]
    [InlineData("PastDue")]
    [InlineData("GracePeriod")]
    [InlineData("Suspended")]
    public void Reactivates_from_each_lapse_state(string state)
    {
        var subscription = DrivenToLapseState(state);
        var newEnd = DateTime.UtcNow.AddMonths(1);

        var result = subscription.ReactivateAfterSelfServicePayment(
            DateTime.UtcNow,
            newEnd,
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public void Fails_from_a_non_lapse_state()
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription
            .StartTrial(Guid.NewGuid(), plan, version, 14, Guid.Empty, DateTime.UtcNow)
            .Value;

        var result = subscription.ReactivateAfterSelfServicePayment(
            DateTime.UtcNow,
            DateTime.UtcNow.AddMonths(1),
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void Rejects_a_period_that_does_not_advance()
    {
        var subscription = DrivenToExpired();
        var now = DateTime.UtcNow;

        var result = subscription.ReactivateAfterSelfServicePayment(now, now, Guid.NewGuid(), now);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.InvalidPeriod", result.Error.Code);
    }

    private static TenantSubscription DrivenToLapseState(string state)
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var now = DateTime.UtcNow;
        var subscription = TenantSubscription.StartTrial(Guid.NewGuid(), plan, version, 14, Guid.Empty, now).Value;
        subscription.ConvertTrialToActive(now, now.AddMonths(1), null, Guid.Empty, now);
        subscription.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, now);
        if (state == "PastDue")
            return subscription;

        subscription.EnterGracePeriodAfterRetriesExhausted(now.AddDays(7), Guid.Empty, now);
        if (state == "GracePeriod")
            return subscription;

        subscription.SuspendBecauseGraceExpired(Guid.Empty, now.AddDays(8));
        return subscription;
    }

    private static TenantSubscription DrivenToExpired()
    {
        var subscription = DrivenToLapseState("Suspended");
        subscription.ExpireAfterSuspensionTimeout(Guid.Empty, DateTime.UtcNow.AddDays(40));
        return subscription;
    }

    private static (SubscriptionPlan Plan, SubscriptionPlanVersion Version) CreatePublishedPlan(string code)
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Standard, Guid.Empty, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, trialDaysDefault: 14, [BillingCycle.Monthly]).Value;
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);
        return (plan, version);
    }
}
