using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForPlan;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

public sealed class RecalculateEntitlementsForPlanHandlerTests
{
    [Fact]
    public async Task Queues_one_recalc_command_per_tenant_of_the_plan()
    {
        var planId = Guid.NewGuid();
        var tenants = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var repo = new FakeSubscriptionRepository(tenants);
        var bus = new CapturingMessageBus();

        // BatchSize 2 fuerza varias vueltas del keyset (2+2+1).
        var result = await RecalculateEntitlementsForPlanHandler.Handle(
            new RecalculateEntitlementsForPlanCommand(planId, BatchSize: 2),
            repo,
            bus,
            NullLogger<RecalculateEntitlementsForPlanCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value);

        var queued = bus.Published.OfType<RecalculateEntitlementsCommand>().Select(c => c.TenantId).ToList();
        Assert.Equal(5, queued.Count);
        Assert.Equal(tenants.OrderBy(t => t), queued.OrderBy(t => t));
    }

    [Fact]
    public async Task Queues_nothing_when_no_tenant_is_on_the_plan()
    {
        var repo = new FakeSubscriptionRepository([]);
        var bus = new CapturingMessageBus();

        var result = await RecalculateEntitlementsForPlanHandler.Handle(
            new RecalculateEntitlementsForPlanCommand(Guid.NewGuid()),
            repo,
            bus,
            NullLogger<RecalculateEntitlementsForPlanCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
        Assert.Empty(bus.Published);
    }

    // Solo GetTenantIdsByPlanAsync es funcional (keyset por Guid); el resto no lo toca este handler.
    private sealed class FakeSubscriptionRepository(IReadOnlyList<Guid> tenantIdsForPlan) : ISubscriptionRepository
    {
        public Task<IReadOnlyList<Guid>> GetTenantIdsByPlanAsync(
            Guid planId,
            Guid afterTenantId,
            int batchSize,
            CancellationToken ct = default
        )
        {
            IReadOnlyList<Guid> page = tenantIdsForPlan
                .Where(id => id.CompareTo(afterTenantId) > 0)
                .OrderBy(id => id)
                .Take(batchSize)
                .ToList();
            return Task.FromResult(page);
        }

        public Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(TenantSubscription subscription, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantSubscription?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetDueForRenewalAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetExpiredTrialsAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetPastGracePeriodAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetSuspendedBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetCancelledPastPeriodEndAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetRenewingBetweenAsync(
            DateTime fromUtc,
            DateTime toUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetAccessEndingBetweenAsync(
            DateTime fromUtc,
            DateTime toUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<(IReadOnlyList<TenantSubscription> Items, int TotalCount)> GetPastDueAsync(
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
