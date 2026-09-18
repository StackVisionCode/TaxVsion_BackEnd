using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForAllPlans;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForPlan;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// The fleet-wide entitlement backfill: one command fans out a per-plan recalc for every published plan.
/// Used to reconcile EXISTING tenants after an entitlement-shape change (e.g. the effective staff cap);
/// new tenants recalc on creation, so this backfill neither needs nor affects them.
/// </summary>
public sealed class RecalculateEntitlementsForAllPlansHandlerTests
{
    [Fact]
    public async Task Queues_one_recalc_per_published_plan()
    {
        var plans = new[]
        {
            CreatePublishedPlan("starter"),
            CreatePublishedPlan("pro"),
            CreatePublishedPlan("enterprise"),
        };
        var bus = new CapturingMessageBus();

        var result = await RecalculateEntitlementsForAllPlansHandler.Handle(
            new RecalculateEntitlementsForAllPlansCommand(),
            new FakePlanRepo(plans),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
        var queued = bus.Published.OfType<RecalculateEntitlementsForPlanCommand>().Select(command => command.PlanId);
        Assert.Equal(plans.Select(plan => plan.Id).OrderBy(id => id), queued.OrderBy(id => id));
    }

    [Fact]
    public async Task Queues_nothing_when_there_are_no_published_plans()
    {
        var bus = new CapturingMessageBus();

        var result = await RecalculateEntitlementsForAllPlansHandler.Handle(
            new RecalculateEntitlementsForAllPlansCommand(),
            new FakePlanRepo([]),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
        Assert.Empty(bus.Published);
    }

    private static SubscriptionPlan CreatePublishedPlan(string code)
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Standard, Guid.Empty, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, versionNumber: 1, trialDaysDefault: 14, [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        Assert.True(plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow).IsSuccess);
        return plan;
    }

    private sealed class FakePlanRepo(IReadOnlyList<SubscriptionPlan> published) : IPlanRepository
    {
        public Task<IReadOnlyList<SubscriptionPlan>> GetPublishedAsync(CancellationToken ct = default) =>
            Task.FromResult(published);

        public Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SubscriptionPlan?> GetByIdAsync(Guid planId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SubscriptionPlan?> GetByIdForUpdateAsync(Guid planId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
