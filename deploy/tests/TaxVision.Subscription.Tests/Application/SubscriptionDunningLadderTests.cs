using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

public sealed class SubscriptionDunningLadderTests
{
    [Fact]
    public async Task Renewal_failure_without_retry_enters_grace_period_and_publishes_lifecycle_event()
    {
        var subscription = CreateActiveSubscription();
        subscription.BeginRenewal("k1", Guid.Empty, DateTime.UtcNow);
        var bus = new CapturingMessageBus();

        await SubscriptionRenewalPaymentFailedConsumer.Handle(
            new SubscriptionRenewalPaymentFailedIntegrationEvent
            {
                TenantId = subscription.TenantId,
                TenantSubscriptionId = subscription.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = "k1",
                FailureCode = "card_declined",
                FailureReason = "Card declined",
                WillRetry = false,
            },
            new FakeSubscriptionRepository(subscription),
            new FakeUnitOfWork(),
            bus,
            Options.Create(new SubscriptionOptions { GracePeriodDays = 7 }),
            new FakeCorrelationContext(),
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.Equal(SubscriptionStatus.GracePeriod, subscription.Status);

        var evt = Assert.Single(bus.Published.OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
        Assert.Equal("Active", evt.PreviousStatus);
        Assert.Equal("GracePeriod", evt.Status);
        Assert.Equal(nameof(SubscriptionChangeReason.RenewalPaymentFailed), evt.Reason);
        Assert.Equal("card_declined", evt.FailureCode);
        Assert.NotNull(evt.GracePeriodEndsAtUtc);
    }

    [Fact]
    public async Task Renewal_success_while_past_due_recovers_to_active_and_publishes_recovery()
    {
        var subscription = CreateActiveSubscription();
        subscription.BeginRenewal("k1", Guid.Empty, DateTime.UtcNow);
        subscription.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, DateTime.UtcNow);
        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        var bus = new CapturingMessageBus();

        await SubscriptionRenewalPaymentSucceededConsumer.Handle(
            new SubscriptionRenewalPaymentSucceededIntegrationEvent
            {
                TenantId = subscription.TenantId,
                TenantSubscriptionId = subscription.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = "k1",
                ExternalPaymentReference = "ext-ref-123",
                PaidAtUtc = DateTime.UtcNow,
            },
            new FakeSubscriptionRepository(subscription),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);

        var evt = Assert.Single(bus.Published.OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
        Assert.Equal("PastDue", evt.PreviousStatus);
        Assert.Equal("Active", evt.Status);
        Assert.Equal(nameof(SubscriptionChangeReason.PaymentRecovered), evt.Reason);
    }

    private static TenantSubscription CreateActiveSubscription()
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("starter").Value, "Starter", "desc", PlanTier.Standard, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);

        return TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                nowUtc,
                nowUtc.AddMonths(1),
                Guid.Empty,
                nowUtc
            )
            .Value;
    }

    private sealed class FakeSubscriptionRepository(TenantSubscription subscription) : ISubscriptionRepository
    {
        public Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<TenantSubscription?>(subscription.TenantId == tenantId ? subscription : null);

        public Task AddAsync(TenantSubscription s, CancellationToken ct = default) => Task.CompletedTask;

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

        public Task<IReadOnlyList<Guid>> GetTenantIdsByPlanAsync(
            Guid planId,
            Guid afterTenantId,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
