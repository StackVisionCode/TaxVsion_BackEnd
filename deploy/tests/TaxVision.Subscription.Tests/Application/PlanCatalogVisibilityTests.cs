using TaxVision.Subscription.Application.Plans.Queries;
using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

// Lo que no está a la venta no se ofrece ni se compra, pero quien ya lo tiene lo conserva.
public sealed class PlanCatalogVisibilityTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task The_public_list_shows_only_plans_on_sale_with_rank_and_featured()
    {
        var starter = Plan("starter", 39m, onSale: false);
        var enterprise = Plan("enterprise", 99m, onSale: true);
        enterprise.SetSalesRank(SalesRank.Create(1).Value, Actor, DateTime.UtcNow);
        enterprise.SetFeatured(true, Actor, DateTime.UtcNow);

        var result = await GetPlansHandler.Handle(
            new GetPlansQuery(),
            new InMemoryPlanRepo(starter, enterprise),
            CancellationToken.None
        );

        var listed = Assert.Single(result.Value);
        Assert.Equal("enterprise", listed.Code);
        Assert.Equal(1, listed.Rank);
        Assert.True(listed.IsFeatured);
    }

    [Fact]
    public async Task Onboarding_pricing_refuses_a_plan_that_is_not_on_sale()
    {
        var pro = Plan("pro", 69m, onSale: false);

        var result = await GetInternalPlanPricingHandler.Handle(
            new GetInternalPlanPricingQuery(pro.Id, BillingCycle.Monthly),
            new InMemoryPlanRepo(pro),
            CancellationToken.None
        );

        Assert.Equal("Subscription.Plan.NotOnSale", result.Error.Code);
    }

    [Fact]
    public async Task Onboarding_pricing_still_prices_a_plan_on_sale()
    {
        var enterprise = Plan("enterprise", 99m, onSale: true);

        var result = await GetInternalPlanPricingHandler.Handle(
            new GetInternalPlanPricingQuery(enterprise.Id, BillingCycle.Monthly),
            new InMemoryPlanRepo(enterprise),
            CancellationToken.None
        );

        Assert.Equal(9900, result.Value.PriceCents);
    }

    // Recibos y correos de Auth necesitan el nombre aunque el plan ya no se venda.
    [Fact]
    public async Task The_internal_summary_names_a_plan_off_sale()
    {
        var pro = Plan("pro", 69m, onSale: false);

        var result = await GetInternalPlanSummaryHandler.Handle(
            new GetInternalPlanSummaryQuery(pro.Id),
            new InMemoryPlanRepo(pro),
            CancellationToken.None
        );

        Assert.Equal(new InternalPlanSummaryResponse(pro.Id, "pro", "pro"), result.Value);
    }

    [Fact]
    public async Task The_internal_summary_of_an_unknown_plan_is_not_found()
    {
        var result = await GetInternalPlanSummaryHandler.Handle(
            new GetInternalPlanSummaryQuery(Guid.NewGuid()),
            new InMemoryPlanRepo(),
            CancellationToken.None
        );

        Assert.Equal("Subscription.Plan.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Changing_to_a_plan_off_sale_is_refused()
    {
        var enterprise = Plan("enterprise", 99m, onSale: true);
        var pro = Plan("pro", 69m, onSale: false);
        var subscription = ActiveOn(enterprise);

        var result = await PlanChangeResolver.ResolveAsync(
            subscription,
            "pro",
            null,
            new InMemoryPlanRepo(enterprise, pro),
            CancellationToken.None
        );

        Assert.Equal("Plan.NotOnSale", result.Error.Code);
    }

    // Quien está en un plan retirado puede cambiar su propio ciclo sin pasarse a otro plan.
    [Fact]
    public async Task Changing_the_cycle_of_the_current_plan_off_sale_is_allowed()
    {
        var pro = Plan("pro", 69m, onSale: false);
        var subscription = ActiveOn(pro);

        var result = await PlanChangeResolver.ResolveAsync(
            subscription,
            "pro",
            "Yearly",
            new InMemoryPlanRepo(pro),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(BillingCycle.Yearly, result.Value.EffectiveCycle);
    }

    private static SubscriptionPlan Plan(string code, decimal monthlyUsd, bool onSale)
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Starter, Actor, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, 1, trialDaysDefault: 14, [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(monthlyUsd, "USD").Value).Value
        );
        version.AddPriceTier(
            PlanPriceTier
                .Create(version.Id, BillingCycle.Yearly, 1, null, Money.Create(monthlyUsd * 10, "USD").Value)
                .Value
        );
        PlanTestData.EnsurePublishableFeature(version);
        plan.AddVersion(version, Actor, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Actor, nowUtc);
        if (onSale)
            plan.PutOnSale(Actor, nowUtc);
        return plan;
    }

    private static TenantSubscription ActiveOn(SubscriptionPlan plan)
    {
        var nowUtc = DateTime.UtcNow;
        return TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                plan.GetPublishedVersion()!,
                BillingCycle.Monthly,
                nowUtc,
                nowUtc.AddDays(30),
                Guid.Empty,
                nowUtc
            )
            .Value;
    }
}
