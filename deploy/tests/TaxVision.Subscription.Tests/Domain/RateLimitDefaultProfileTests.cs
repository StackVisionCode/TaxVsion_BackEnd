using BuildingBlocks.RateLimiting;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Plans.Events;
using TaxVision.Subscription.Domain.RateLimiting;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

// D12: ningún plan, nuevo o viejo, queda por debajo del cupo base. El perfil por defecto es el de Enterprise.
public sealed class RateLimitDefaultProfileTests
{
    [Theory]
    [InlineData(0.99)]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_multiplier_below_one_is_rejected(decimal value)
    {
        Assert.Equal("RateLimit.MultiplierBelowBase", RateLimitMultiplier.Create(value).Error.Code);
    }

    [Fact]
    public void One_is_the_base_and_is_allowed()
    {
        Assert.Equal(1.0m, RateLimitMultiplier.Create(1.0m).Value.Value);
    }

    [Fact]
    public void The_seeded_profile_carries_the_enterprise_multipliers_for_every_scalable_category()
    {
        var profile = RateLimitDefaultProfile.Seed(DateTime.UtcNow);

        Assert.Equal(
            [
                RateLimitCategory.F,
                RateLimitCategory.G,
                RateLimitCategory.H,
                RateLimitCategory.I,
                RateLimitCategory.J,
                RateLimitCategory.K,
                RateLimitCategory.L,
                RateLimitCategory.M,
                RateLimitCategory.N,
                RateLimitCategory.O,
            ],
            RateLimitDefaultProfile.ScalableCategories
        );
        Assert.Equal(10m, profile.MultiplierFor(RateLimitCategory.F).Value);
        Assert.Equal(20m, profile.MultiplierFor(RateLimitCategory.K).Value);
        Assert.Equal(10m, profile.MultiplierFor(RateLimitCategory.L).Value);
        Assert.Equal(1m, profile.MultiplierFor(RateLimitCategory.M).Value);
        Assert.Equal(1m, profile.MultiplierFor(RateLimitCategory.N).Value);
        Assert.Equal(10m, profile.MultiplierFor(RateLimitCategory.O).Value);
    }

    [Fact]
    public void Revising_changes_one_category_only()
    {
        var profile = RateLimitDefaultProfile.Seed(DateTime.UtcNow);
        var actor = Guid.NewGuid();

        var result = profile.Revise(RateLimitCategory.G, RateLimitMultiplier.Create(12m).Value, actor, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(12m, profile.MultiplierFor(RateLimitCategory.G).Value);
        Assert.Equal(10m, profile.MultiplierFor(RateLimitCategory.F).Value);
        Assert.Equal(actor, profile.UpdatedBy);
    }

    // A-E son pre-auth y webhooks: nunca escalan por plan.
    [Fact]
    public void A_category_that_never_scales_cannot_be_revised()
    {
        var profile = RateLimitDefaultProfile.Seed(DateTime.UtcNow);

        var result = profile.Revise(
            RateLimitCategory.A,
            RateLimitMultiplier.Create(2m).Value,
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.Equal("RateLimit.CategoryNotScalable", result.Error.Code);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0)]
    public void A_plan_row_cannot_go_below_the_base_either(decimal multiplier)
    {
        var seeded = PlanRateLimit.Seed(Guid.NewGuid(), PlanCode.Create("pro").Value, RateLimitCategory.F, multiplier);
        var row = PlanRateLimit.Seed(Guid.NewGuid(), PlanCode.Create("pro").Value, RateLimitCategory.F, 5m).Value;

        Assert.Equal("RateLimit.MultiplierBelowBase", seeded.Error.Code);
        Assert.Equal("RateLimit.MultiplierBelowBase", row.Revise(multiplier, null).Error.Code);
        Assert.Equal(5m, row.MultiplierOverride);
    }

    [Fact]
    public void Creating_a_plan_announces_it_so_its_rate_limits_get_provisioned()
    {
        var actor = Guid.NewGuid();

        var plan = SubscriptionPlan
            .Create(PlanCode.Create("growth").Value, "Growth", "Growth plan", PlanTier.Pro, actor, DateTime.UtcNow)
            .Value;

        var created = Assert.IsType<PlanCreatedDomainEvent>(Assert.Single(plan.DomainEvents));
        Assert.Equal(plan.Id, created.PlanId);
        Assert.Equal("growth", created.PlanCode);
        Assert.Equal(actor, created.ActorUserId);
    }
}
