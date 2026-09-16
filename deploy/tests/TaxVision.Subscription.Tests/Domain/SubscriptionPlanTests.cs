using System.Linq;
using TaxVision.Subscription.Domain.Entitlements;
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

    [Fact]
    public void ReviseModules_publishes_a_new_version_with_the_new_modules_and_keeps_the_rest()
    {
        var plan = CreatePlan("starter");
        var v1 = CreateVersion(plan.Id, versionNumber: 1);
        v1.AddEntitlementDefinition(
            PlanEntitlementDefinition
                .Create(v1.Id, EntitlementKey.Create("seats.max").Value, EntitlementValueType.Int, "5", "seats.max")
                .Value
        );
        v1.AddPriceTier(
            PlanPriceTier.Create(v1.Id, BillingCycle.Monthly, 1, null, Money.Create(10m, "USD").Value).Value
        );
        v1.AddFeature(PlanFeature.Create(v1.Id, EntitlementKey.Create("core.chat").Value, true, "core.chat").Value);
        v1.AddFeature(
            PlanFeature.Create(v1.Id, EntitlementKey.Create("module.signatures").Value, true, "module.signatures").Value
        );
        plan.AddVersion(v1, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(v1.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        var result = plan.ReviseModules(["documents"], Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanVersionStatus.Superseded, v1.Status);

        var published = plan.GetPublishedVersion()!;
        Assert.NotEqual(v1.Id, published.Id);
        Assert.Equal(2, published.VersionNumber);

        var featureKeys = published.Features.Select(f => f.FeatureKey.Value).ToList();
        Assert.Contains("module.documents", featureKeys);
        Assert.DoesNotContain("module.signatures", featureKeys);
        Assert.Contains("core.chat", featureKeys); // feature no-módulo conservada
        Assert.Single(published.Entitlements); // límite conservado
        Assert.Single(published.PriceTiers); // precio conservado
    }

    [Fact]
    public void ReviseModules_fails_when_there_is_no_published_version()
    {
        var plan = CreatePlan("starter");

        var result = plan.ReviseModules(["documents"], Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Plan.NoPublishedVersion", result.Error.Code);
    }

    [Fact]
    public void RevisePrices_publishes_a_new_version_with_the_new_prices_and_keeps_modules_and_limits()
    {
        var plan = CreatePlan("starter");
        var v1 = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly, BillingCycle.Yearly]).Value;
        v1.AddEntitlementDefinition(
            PlanEntitlementDefinition
                .Create(v1.Id, EntitlementKey.Create("seats.max").Value, EntitlementValueType.Int, "5", "seats.max")
                .Value
        );
        v1.AddFeature(
            PlanFeature.Create(v1.Id, EntitlementKey.Create("module.signatures").Value, true, "module.signatures").Value
        );
        v1.AddPriceTier(
            PlanPriceTier.Create(v1.Id, BillingCycle.Monthly, 1, null, Money.Create(49m, "USD").Value).Value
        );
        v1.AddPriceTier(
            PlanPriceTier.Create(v1.Id, BillingCycle.Yearly, 1, null, Money.Create(490m, "USD").Value).Value
        );
        plan.AddVersion(v1, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(v1.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);

        var result = plan.RevisePrices(59m, 590m, Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanVersionStatus.Superseded, v1.Status);

        var published = plan.GetPublishedVersion()!;
        Assert.Equal(2, published.VersionNumber);
        var monthly = Assert.Single(published.PriceTiers, t => t.BillingCycle == BillingCycle.Monthly);
        var yearly = Assert.Single(published.PriceTiers, t => t.BillingCycle == BillingCycle.Yearly);
        Assert.Equal(59m, monthly.UnitAmount.Amount);
        Assert.Equal(590m, yearly.UnitAmount.Amount);
        Assert.Contains("module.signatures", published.Features.Select(f => f.FeatureKey.Value)); // módulo conservado
        Assert.Single(published.Entitlements); // límite conservado
    }

    [Fact]
    public void RevisePrices_fails_when_there_is_no_published_version()
    {
        var plan = CreatePlan("starter");

        var result = plan.RevisePrices(59m, 590m, Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Plan.NoPublishedVersion", result.Error.Code);
    }

    private static SubscriptionPlan CreatePlan(string code) =>
        SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Standard, Guid.Empty, DateTime.UtcNow)
            .Value;

    private static SubscriptionPlanVersion CreateVersion(Guid planId, int versionNumber) =>
        SubscriptionPlanVersion.Create(planId, versionNumber, trialDaysDefault: 14, [BillingCycle.Monthly]).Value;
}
