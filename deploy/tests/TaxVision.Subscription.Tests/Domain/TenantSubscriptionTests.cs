using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class TenantSubscriptionTests
{
    [Fact]
    public void StartTrial_uses_plan_and_requested_duration()
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var before = DateTime.UtcNow;

        var result = TenantSubscription.StartTrial(Guid.NewGuid(), plan, version, trialDays: 14, Guid.Empty, before);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Trialing, result.Value.Status);
        Assert.Equal("starter", result.Value.PlanCode);
        Assert.Equal(before.AddDays(14), result.Value.TrialEndsAtUtc);
    }

    [Fact]
    public void ChangePlan_from_trial_keeps_trialing_status()
    {
        var (starter, starterVersion) = CreatePublishedPlan("starter");
        var (pro, proVersion) = CreatePublishedPlan("pro");
        var subscription = TenantSubscription.StartTrial(Guid.NewGuid(), starter, starterVersion, 14, Guid.Empty, DateTime.UtcNow).Value;

        var result = subscription.ChangePlan(pro, proVersion, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status);
        Assert.Equal("pro", subscription.PlanCode);
    }

    [Fact]
    public void SuspendForPolicyViolation_during_trial_is_allowed()
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription.StartTrial(Guid.NewGuid(), plan, version, 14, Guid.Empty, DateTime.UtcNow).Value;

        var result = subscription.SuspendForPolicyViolation("abuse", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
    }

    [Fact]
    public void CancelImmediately_on_an_already_cancelled_subscription_fails()
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription.StartTrial(Guid.NewGuid(), plan, version, 14, Guid.Empty, DateTime.UtcNow).Value;
        subscription.CancelImmediately("tenant requested", Guid.Empty, DateTime.UtcNow);

        var result = subscription.CancelImmediately("tenant requested again", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void ReactivateAfterAdminReview_requires_suspended_status()
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription.StartTrial(Guid.NewGuid(), plan, version, 14, Guid.Empty, DateTime.UtcNow).Value;

        var result = subscription.ReactivateAfterAdminReview(DateTime.UtcNow, DateTime.UtcNow.AddMonths(1), Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.InvalidTransition", result.Error.Code);
    }

    private static (SubscriptionPlan Plan, SubscriptionPlanVersion Version) CreatePublishedPlan(string code)
    {
        var plan = SubscriptionPlan.Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Standard, Guid.Empty, DateTime.UtcNow).Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, trialDaysDefault: 14, [BillingCycle.Monthly]).Value;
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);
        return (plan, version);
    }
}
