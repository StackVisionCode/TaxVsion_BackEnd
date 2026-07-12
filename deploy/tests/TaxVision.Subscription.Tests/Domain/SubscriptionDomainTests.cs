using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class SubscriptionDomainTests
{
    [Fact]
    public void Trial_uses_plan_and_requested_duration()
    {
        var plan = CreatePlan("starter", 3);
        var before = DateTime.UtcNow;

        var result = TenantSubscription.StartTrial(Guid.NewGuid(), plan, 14);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Trial, result.Value.Status);
        Assert.Equal("starter", result.Value.PlanCode);
        Assert.InRange(
            result.Value.TrialEndsAtUtc!.Value,
            before.AddDays(14),
            DateTime.UtcNow.AddDays(14).AddSeconds(1)
        );
    }

    [Fact]
    public void Changing_plan_activates_a_trial()
    {
        var subscription = TenantSubscription.StartTrial(Guid.NewGuid(), CreatePlan("starter", 3), 14).Value;

        var result = subscription.ChangePlan(CreatePlan("pro", 10));

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal("pro", subscription.PlanCode);
        Assert.Null(subscription.TrialEndsAtUtc);
    }

    [Fact]
    public void Suspended_subscription_cannot_purchase_seats()
    {
        var subscription = TenantSubscription.StartTrial(Guid.NewGuid(), CreatePlan("starter", 3), 14).Value;
        subscription.Suspend("payment overdue");

        var result = subscription.AddSeats(1);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.Inactive", result.Error.Code);
    }

    private static Plan CreatePlan(string code, int maxUsers) =>
        Plan.Seed(Guid.NewGuid(), code, code, $"{code} plan", 10m, maxUsers, 5, 10L * 1024 * 1024 * 1024, "[]", 1);
}
