using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class SubscriptionPlanTests
{
    [Fact]
    public void Create_starts_in_draft_status()
    {
        var plan = CreatePlan("starter");

        Assert.Equal(PlanStatus.Draft, plan.Status);
        Assert.Null(plan.GetPublishedVersion());
    }

    [Fact]
    public void Publishing_a_version_moves_the_plan_to_published()
    {
        var plan = CreatePlan("starter");
        var version = CreateVersion(plan.Id, versionNumber: 1);
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);

        var result = plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanStatus.Published, plan.Status);
        Assert.Equal(version.Id, plan.GetPublishedVersion()!.Id);
    }

    [Fact]
    public void Publishing_a_new_version_supersedes_the_previous_one()
    {
        var plan = CreatePlan("starter");
        var v1 = CreateVersion(plan.Id, versionNumber: 1);
        var v2 = CreateVersion(plan.Id, versionNumber: 2);
        plan.AddVersion(v1, Guid.Empty, DateTime.UtcNow);
        plan.AddVersion(v2, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(v1.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        var result = plan.PublishVersion(v2.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanVersionStatus.Superseded, v1.Status);
        Assert.Equal(PlanVersionStatus.Published, v2.Status);
        Assert.Equal(v2.Id, plan.GetPublishedVersion()!.Id);
    }

    [Fact]
    public void Archiving_a_plan_that_is_not_deprecated_fails()
    {
        var plan = CreatePlan("starter");

        var result = plan.Archive(Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Plan.NotDeprecated", result.Error.Code);
    }

    private static SubscriptionPlan CreatePlan(string code) =>
        SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Standard, Guid.Empty, DateTime.UtcNow)
            .Value;

    private static SubscriptionPlanVersion CreateVersion(Guid planId, int versionNumber) =>
        SubscriptionPlanVersion.Create(planId, versionNumber, trialDaysDefault: 14, [BillingCycle.Monthly]).Value;
}
