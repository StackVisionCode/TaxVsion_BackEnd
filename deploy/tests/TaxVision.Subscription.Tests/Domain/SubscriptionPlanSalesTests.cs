using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Plans.Events;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

// "A la venta" es reversible y aparte del estado del plan: Deprecate queda solo para fin de vida.
public sealed class SubscriptionPlanSalesTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void A_new_plan_starts_off_sale_with_the_default_rank()
    {
        var plan = DraftPlan("starter");

        Assert.False(plan.IsOnSale);
        Assert.False(plan.IsFeatured);
        Assert.Equal(SalesRank.Default.Value, plan.SalesRank.Value);
    }

    [Fact]
    public void A_plan_without_a_published_version_cannot_go_on_sale()
    {
        var plan = DraftPlan("starter");

        var result = plan.PutOnSale(Actor, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Plan.NoPublishedVersion", result.Error.Code);
        Assert.False(plan.IsOnSale);
    }

    [Fact]
    public void Putting_a_published_plan_on_sale_raises_one_event_and_is_idempotent()
    {
        var plan = PublishedPlan("enterprise");

        Assert.True(plan.PutOnSale(Actor, DateTime.UtcNow).IsSuccess);
        Assert.True(plan.PutOnSale(Actor, DateTime.UtcNow).IsSuccess);

        Assert.True(plan.IsOnSale);
        var raised = Assert.Single(plan.DomainEvents);
        var evt = Assert.IsType<PlanPutOnSaleDomainEvent>(raised);
        Assert.Equal(plan.Id, evt.PlanId);
        Assert.Equal("enterprise", evt.PlanCode);
        Assert.Equal(Actor, evt.ActorUserId);
    }

    [Fact]
    public void A_deprecated_plan_cannot_go_back_on_sale()
    {
        var plan = PublishedPlan("pro");
        plan.Deprecate(Actor, DateTime.UtcNow);

        var result = plan.PutOnSale(Actor, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Plan.NotSellable", result.Error.Code);
    }

    [Fact]
    public void Withdrawing_from_sale_raises_one_event_and_is_idempotent()
    {
        var plan = OnSalePlan("pro");

        Assert.True(plan.WithdrawFromSale(Actor, DateTime.UtcNow).IsSuccess);
        Assert.True(plan.WithdrawFromSale(Actor, DateTime.UtcNow).IsSuccess);

        Assert.False(plan.IsOnSale);
        Assert.IsType<PlanWithdrawnFromSaleDomainEvent>(Assert.Single(plan.DomainEvents));
        // Retirar no toca el ciclo de vida: los clientes actuales siguen y se puede volver a vender.
        Assert.Equal(PlanStatus.Published, plan.Status);
        Assert.True(plan.PutOnSale(Actor, DateTime.UtcNow).IsSuccess);
    }

    [Fact]
    public void Deprecating_a_plan_on_sale_also_withdraws_it()
    {
        var plan = OnSalePlan("starter");

        plan.Deprecate(Actor, DateTime.UtcNow);

        Assert.False(plan.IsOnSale);
        Assert.IsType<PlanWithdrawnFromSaleDomainEvent>(Assert.Single(plan.DomainEvents));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void A_sales_rank_outside_1_to_1000_is_rejected(int value)
    {
        var rank = SalesRank.Create(value);

        Assert.True(rank.IsFailure);
        Assert.Equal("SalesRank.Invalid", rank.Error.Code);
    }

    [Fact]
    public void Changing_the_rank_raises_an_event_only_when_it_changes()
    {
        var plan = OnSalePlan("enterprise");

        plan.SetSalesRank(SalesRank.Create(3).Value, Actor, DateTime.UtcNow);
        plan.SetSalesRank(SalesRank.Create(3).Value, Actor, DateTime.UtcNow);

        Assert.Equal(3, plan.SalesRank.Value);
        var evt = Assert.IsType<PlanSalesRankChangedDomainEvent>(Assert.Single(plan.DomainEvents));
        Assert.Equal(SalesRank.Default.Value, evt.PreviousRank);
        Assert.Equal(3, evt.Rank);
    }

    [Fact]
    public void Featuring_raises_an_event_only_when_it_changes()
    {
        var plan = OnSalePlan("enterprise");

        plan.SetFeatured(true, Actor, DateTime.UtcNow);
        plan.SetFeatured(true, Actor, DateTime.UtcNow);

        Assert.True(plan.IsFeatured);
        var evt = Assert.IsType<PlanFeaturedChangedDomainEvent>(Assert.Single(plan.DomainEvents));
        Assert.True(evt.IsFeatured);
    }

    [Fact]
    public void The_last_plan_on_sale_cannot_be_withdrawn()
    {
        var plan = OnSalePlan("enterprise");

        var result = PlanSalesRules.EnsureCanWithdraw(plan, otherPlansOnSale: 0, trialPlanCode: "starter");

        Assert.True(result.IsFailure);
        Assert.Equal("Plan.LastOnSale", result.Error.Code);
    }

    [Fact]
    public void The_trial_plan_cannot_be_withdrawn()
    {
        var plan = OnSalePlan("enterprise");

        var result = PlanSalesRules.EnsureCanWithdraw(plan, otherPlansOnSale: 2, trialPlanCode: "enterprise");

        Assert.True(result.IsFailure);
        Assert.Equal("Plan.IsTrialPlan", result.Error.Code);
    }

    [Fact]
    public void A_plan_can_be_withdrawn_when_others_stay_on_sale()
    {
        var plan = OnSalePlan("pro");

        Assert.True(PlanSalesRules.EnsureCanWithdraw(plan, otherPlansOnSale: 1, trialPlanCode: "enterprise").IsSuccess);
    }

    [Fact]
    public void A_plan_already_off_sale_passes_the_withdraw_rule()
    {
        var plan = PublishedPlan("pro");

        Assert.True(PlanSalesRules.EnsureCanWithdraw(plan, otherPlansOnSale: 0, trialPlanCode: "pro").IsSuccess);
    }

    // Plan ya creado y guardado: el aviso de "plan creado" ya salió.
    private static SubscriptionPlan DraftPlan(string code)
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Starter, Actor, DateTime.UtcNow)
            .Value;
        plan.ClearDomainEvents();
        return plan;
    }

    private static SubscriptionPlan PublishedPlan(string code)
    {
        var plan = DraftPlan(code);
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, trialDaysDefault: 14, [BillingCycle.Monthly]).Value;
        PlanTestData.EnsurePublishableFeature(version);
        PlanTestData.EnsurePublishablePrice(version);
        plan.AddVersion(version, Actor, DateTime.UtcNow);
        plan.PublishVersion(version.Id, DateTime.UtcNow, Actor, DateTime.UtcNow);
        return plan;
    }

    private static SubscriptionPlan OnSalePlan(string code)
    {
        var plan = PublishedPlan(code);
        plan.PutOnSale(Actor, DateTime.UtcNow);
        plan.ClearDomainEvents();
        return plan;
    }
}
