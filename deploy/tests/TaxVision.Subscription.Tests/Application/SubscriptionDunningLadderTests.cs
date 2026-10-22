using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task Renewal_failure_without_retry_suspends_and_publishes_lifecycle_event()
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
            new FakePlanRepo(null),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);

        var evt = Assert.Single(bus.Published.OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
        Assert.Equal("Active", evt.PreviousStatus);
        Assert.Equal("Suspended", evt.Status);
        Assert.Equal(nameof(SubscriptionChangeReason.RenewalPaymentFailed), evt.Reason);
        Assert.Equal("card_declined", evt.FailureCode);
    }

    // Un correo por cada cobro automático fallido: con el nombre del plan y la fecha del próximo intento.
    [Fact]
    public async Task Automatic_failure_with_a_retry_asks_for_the_failed_charge_email_without_suspending()
    {
        var subscription = CreateActiveSubscription(out var plan);
        subscription.BeginRenewal("k1", Guid.Empty, DateTime.UtcNow);
        var bus = new CapturingMessageBus();
        var nextRetry = DateTime.UtcNow.AddHours(1);

        await FailAutomatically(subscription, plan, bus, willRetry: true, nextRetry);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Empty(bus.Published.OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
        var email = Assert.Single(bus.Published.OfType<RenewalChargeFailedIntegrationEvent>());
        Assert.Equal(RenewalChargeKinds.Plan, email.ChargeKind);
        Assert.Equal("Starter plan", email.ItemDescription);
        Assert.False(email.IsFinal);
        Assert.Equal(nextRetry, email.NextRetryAtUtc);
        Assert.Null(email.Consequence);
        Assert.Equal(2, email.AttemptNumber);
        Assert.Equal("4242", email.CardLast4);
    }

    // El último intento manda UN correo que ya dice que se suspendió: el de "suspended" no se repite.
    [Fact]
    public async Task Final_automatic_failure_reports_the_suspension_in_the_same_email()
    {
        var subscription = CreateActiveSubscription(out var plan);
        subscription.BeginRenewal("k1", Guid.Empty, DateTime.UtcNow);
        var bus = new CapturingMessageBus();

        await FailAutomatically(subscription, plan, bus, willRetry: false, null);

        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
        var email = Assert.Single(bus.Published.OfType<RenewalChargeFailedIntegrationEvent>());
        Assert.True(email.IsFinal);
        Assert.Equal(RenewalChargeConsequences.SubscriptionSuspended, email.Consequence);
        var status = Assert.Single(bus.Published.OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
        Assert.True(status.NotifiedByChargeFailedEmail);
    }

    // En un checkout hosteado el usuario paga en pantalla: ni correo de cobro fallido, y el de suspensión sigue.
    [Fact]
    public async Task A_hosted_checkout_failure_sends_no_failed_charge_email()
    {
        var subscription = CreateActiveSubscription(out var plan);
        subscription.BeginRenewal("k1", Guid.Empty, DateTime.UtcNow);
        var bus = new CapturingMessageBus();

        await FailAutomatically(subscription, plan, bus, willRetry: false, null, automatic: false);

        Assert.Empty(bus.Published.OfType<RenewalChargeFailedIntegrationEvent>());
        Assert.False(
            Assert
                .Single(bus.Published.OfType<TenantSubscriptionStatusChangedIntegrationEvent>())
                .NotifiedByChargeFailedEmail
        );
    }

    private static Task FailAutomatically(
        TenantSubscription subscription,
        SubscriptionPlan plan,
        CapturingMessageBus bus,
        bool willRetry,
        DateTime? nextRetryAtUtc,
        bool automatic = true
    ) =>
        SubscriptionRenewalPaymentFailedConsumer.Handle(
            new SubscriptionRenewalPaymentFailedIntegrationEvent
            {
                TenantId = subscription.TenantId,
                TenantSubscriptionId = subscription.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = "k1",
                FailureCode = "card_declined",
                FailureReason = "Card declined",
                WillRetry = willRetry,
                NextRetryAtUtc = nextRetryAtUtc,
                AttemptNumber = 2,
                MaxAttempts = 4,
                AmountCents = 4900,
                Currency = "USD",
                CardBrand = "visa",
                CardLast4 = "4242",
                IsAutomaticCharge = automatic,
            },
            new FakeSubscriptionRepository(subscription),
            new FakePlanRepo(plan),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

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

    private static TenantSubscription CreateActiveSubscription() => CreateActiveSubscription(out _);

    private static TenantSubscription CreateActiveSubscription(out SubscriptionPlan plan)
    {
        var nowUtc = DateTime.UtcNow;
        plan = SubscriptionPlan
            .Create(PlanCode.Create("starter").Value, "Starter", "desc", PlanTier.Starter, Guid.Empty, nowUtc)
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

        public Task<IReadOnlyList<TenantSubscription>> GetSuspendedBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        // El job de anti-entropía no participa en este test.
        public Task<IReadOnlyList<TenantSubscription>> GetByStatusesAsync(
            IReadOnlyCollection<SubscriptionStatus> statuses,
            Guid afterTenantId,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetActiveWithPeriodEndedBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TenantSubscription>>([]);

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
