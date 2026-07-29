using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>PayFlow (Fase 17) — compensación de un onboarding cancelado que ya había activado
/// una suscripción.</summary>
public sealed class OnboardingCancelRequestedConsumerTests
{
    [Fact]
    public async Task Cancels_the_subscription_when_it_exists_for_the_onboarding()
    {
        var onboardingId = Guid.NewGuid();
        var subscription = ActivateSubscription(Guid.NewGuid(), CreatePublishedPlan("starter"), onboardingId);
        var subscriptions = new FakeSubscriptionRepository { Existing = subscription };
        var unitOfWork = new FakeUnitOfWork();

        var evt = new OnboardingCancelRequestedIntegrationEvent
        {
            OnboardingId = onboardingId,
            Reason = "Provisioning failed permanently",
            OnboardingTenantId = Guid.NewGuid(),
            OnboardingUserId = Guid.NewGuid(),
            OnboardingSubscriptionId = subscription.Id,
        };

        await OnboardingCancelRequestedConsumer.Handle(
            evt,
            subscriptions,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Is_a_noop_when_the_subscription_step_never_ran()
    {
        var subscriptions = new FakeSubscriptionRepository();
        var unitOfWork = new FakeUnitOfWork();

        var evt = new OnboardingCancelRequestedIntegrationEvent
        {
            OnboardingId = Guid.NewGuid(),
            Reason = "Provisioning failed at Tenant step",
            OnboardingTenantId = null,
            OnboardingUserId = null,
            OnboardingSubscriptionId = null,
        };

        await OnboardingCancelRequestedConsumer.Handle(
            evt,
            subscriptions,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Logs_and_returns_when_no_subscription_is_found_for_the_onboarding()
    {
        var subscriptions = new FakeSubscriptionRepository();
        var unitOfWork = new FakeUnitOfWork();

        var evt = new OnboardingCancelRequestedIntegrationEvent
        {
            OnboardingId = Guid.NewGuid(),
            Reason = "Provisioning failed permanently",
            OnboardingTenantId = Guid.NewGuid(),
            OnboardingUserId = Guid.NewGuid(),
            OnboardingSubscriptionId = Guid.NewGuid(),
        };

        await OnboardingCancelRequestedConsumer.Handle(
            evt,
            subscriptions,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private static SubscriptionPlan CreatePublishedPlan(string code)
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Standard, Guid.Empty, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, versionNumber: 1, trialDaysDefault: 14, [BillingCycle.Monthly])
            .Value;
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        var publishResult = plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);
        if (publishResult.IsFailure)
            throw new InvalidOperationException("Test setup failure: could not publish the plan version.");

        return plan;
    }

    private static TenantSubscription ActivateSubscription(Guid tenantId, SubscriptionPlan plan, Guid onboardingId)
    {
        var nowUtc = DateTime.UtcNow;
        return TenantSubscription
            .ActivateImmediately(
                tenantId,
                plan,
                plan.GetPublishedVersion()!,
                BillingCycle.Monthly,
                periodStartUtc: nowUtc,
                periodEndUtc: nowUtc.AddMonths(1),
                actorUserId: Guid.Empty,
                nowUtc,
                onboardingId
            )
            .Value;
    }

    private sealed class FakeSubscriptionRepository : ISubscriptionRepository
    {
        public TenantSubscription? Existing { get; set; }

        public Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(TenantSubscription subscription, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantSubscription?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            Task.FromResult(Existing is not null && Existing.OnboardingId == onboardingId ? Existing : null);

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

        public Task<(IReadOnlyList<TenantSubscription> Items, int TotalCount)> GetPastDueAsync(
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
