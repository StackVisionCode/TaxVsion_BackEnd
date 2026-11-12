using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

// D6: entre planes distintos decide el rango; el cambio de ciclo se queda en la edición firmada.
public sealed class PlanChangeDirectionTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task A_higher_ranked_plan_is_an_upgrade_even_if_it_is_cheaper()
    {
        var pro = Plan("pro", rank: 2, monthlyUsd: 129m);
        var enterprise = Plan("enterprise", rank: 3, monthlyUsd: 99m);

        var result = await Resolve(ActiveOn(pro), "enterprise", null, pro, enterprise);

        Assert.Equal(PlanChangeDirection.Upgrade, result.Value.Direction);
    }

    [Fact]
    public async Task A_lower_ranked_plan_is_a_downgrade_even_if_it_is_pricier()
    {
        var pro = Plan("pro", rank: 2, monthlyUsd: 129m);
        var enterprise = Plan("enterprise", rank: 3, monthlyUsd: 99m);

        var result = await Resolve(ActiveOn(enterprise), "pro", null, pro, enterprise);

        Assert.Equal(PlanChangeDirection.Downgrade, result.Value.Direction);
    }

    [Fact]
    public async Task With_the_same_rank_the_monthly_equivalent_decides()
    {
        var basic = Plan("basic", rank: 5, monthlyUsd: 50m);
        var plus = Plan("plus", rank: 5, monthlyUsd: 60m);

        var result = await Resolve(ActiveOn(basic), "plus", null, basic, plus);

        Assert.Equal(PlanChangeDirection.Upgrade, result.Value.Direction);
    }

    // Moverse de edición es una migración explícita (F6), no un efecto de pedir el mismo plan.
    [Fact]
    public async Task Asking_for_the_same_plan_on_an_older_edition_changes_nothing()
    {
        var pro = Plan("pro", rank: 2, monthlyUsd: 129m);
        var subscription = ActiveOn(pro);
        PlanTestData.PublishNewEdition(pro, monthlyUsd: 69m);

        var result = await Resolve(subscription, "pro", null, pro);

        Assert.Equal(PlanChangeDirection.None, result.Value.Direction);
        Assert.Equal(subscription.PlanVersionId, result.Value.PlanVersion.Id);
    }

    [Fact]
    public async Task A_cycle_change_keeps_the_contracted_edition_and_its_price()
    {
        var pro = Plan("pro", rank: 2, monthlyUsd: 129m, yearlyUsd: 1290m);
        var subscription = ActiveOn(pro);
        PlanTestData.PublishNewEdition(pro, monthlyUsd: 69m);

        var result = await Resolve(subscription, "pro", "Yearly", pro);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(subscription.PlanVersionId, result.Value.PlanVersion.Id);
        Assert.Equal(129000, result.Value.TargetAmountCents);
        // Como hoy: el anual completo cuesta más que el mensual completo, se cobra ya.
        Assert.Equal(PlanChangeDirection.Upgrade, result.Value.Direction);
    }

    [Fact]
    public async Task A_cycle_the_contracted_edition_does_not_price_is_refused()
    {
        var pro = Plan("pro", rank: 2, monthlyUsd: 129m);
        var subscription = ActiveOn(pro);
        PublishEditionWithYearly(pro);

        var result = await Resolve(subscription, "pro", "Yearly", pro);

        Assert.Equal("Plan.CycleNotAvailableForContractedVersion", result.Error.Code);
    }

    private static Task<BuildingBlocks.Results.Result<PlanChangeResolution>> Resolve(
        TenantSubscription subscription,
        string planCode,
        string? cycle,
        params SubscriptionPlan[] plans
    ) =>
        PlanChangeResolver.ResolveAsync(
            subscription,
            planCode,
            cycle,
            new InMemoryPlanRepo(plans),
            CancellationToken.None
        );

    private static SubscriptionPlan Plan(string code, int rank, decimal monthlyUsd, decimal? yearlyUsd = null)
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Starter, Actor, nowUtc)
            .Value;
        BillingCycle[] cycles = yearlyUsd is null
            ? [BillingCycle.Monthly]
            : [BillingCycle.Monthly, BillingCycle.Yearly];
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, cycles).Value;
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(monthlyUsd, "USD").Value).Value
        );
        if (yearlyUsd is { } yearly)
            version.AddPriceTier(
                PlanPriceTier.Create(version.Id, BillingCycle.Yearly, 1, null, Money.Create(yearly, "USD").Value).Value
            );
        PlanTestData.EnsurePublishableFeature(version);
        plan.AddVersion(version, Actor, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Actor, nowUtc);
        plan.PutOnSale(Actor, nowUtc);
        plan.SetSalesRank(SalesRank.Create(rank).Value, Actor, nowUtc);
        return plan;
    }

    private static void PublishEditionWithYearly(SubscriptionPlan plan)
    {
        var nowUtc = DateTime.UtcNow;
        var next = plan.Versions.Max(v => v.VersionNumber) + 1;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, next, 14, [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(99m, "USD").Value).Value
        );
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Yearly, 1, null, Money.Create(999m, "USD").Value).Value
        );
        PlanTestData.EnsurePublishableFeature(version);
        plan.AddVersion(version, Actor, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Actor, nowUtc);
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
