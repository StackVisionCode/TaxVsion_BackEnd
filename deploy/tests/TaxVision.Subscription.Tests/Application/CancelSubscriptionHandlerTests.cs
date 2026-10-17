using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.Commands.Cancel;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

public sealed class CancelSubscriptionHandlerTests
{
    private sealed class CountingCancellationClient : IHostedCheckoutCancellationClient
    {
        public int Calls { get; private set; }

        public Task<Result> CancelAsync(Guid tenantId, Guid saaSPaymentId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task First_cancel_publishes_event_and_audits()
    {
        var tenantId = Guid.NewGuid();
        var subscription = ActiveSubscription(tenantId, out _, out _);
        var repo = new FakeSubscriptionRepo(subscription);
        var cancellation = new CountingCancellationClient();
        var bus = new CapturingMessageBus();
        var audit = new FakeSubscriptionAuditLogWriter();

        var result = await CancelSubscriptionHandler.Handle(
            new CancelSubscriptionCommand(tenantId, "too expensive", Guid.NewGuid()),
            repo,
            cancellation,
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            audit,
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.Single(audit.Entries);
        Assert.Contains(bus.Published, m => m is TenantSubscriptionStatusChangedIntegrationEvent);
    }

    [Fact]
    public async Task Second_cancel_is_idempotent_and_does_not_republish()
    {
        var tenantId = Guid.NewGuid();
        var subscription = ActiveSubscription(tenantId, out _, out _);
        var repo = new FakeSubscriptionRepo(subscription);
        var cancellation = new CountingCancellationClient();
        var bus = new CapturingMessageBus();
        var audit = new FakeSubscriptionAuditLogWriter();

        await CancelSubscriptionHandler.Handle(
            new CancelSubscriptionCommand(tenantId, "too expensive", Guid.NewGuid()),
            repo,
            cancellation,
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            audit,
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );
        var publishedBefore = bus.Published.Count;
        var auditBefore = audit.Entries.Count;

        var second = await CancelSubscriptionHandler.Handle(
            new CancelSubscriptionCommand(tenantId, "clicked again", Guid.NewGuid()),
            repo,
            cancellation,
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            audit,
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.True(second.IsSuccess);
        Assert.Equal(publishedBefore, bus.Published.Count);
        Assert.Equal(auditBefore, audit.Entries.Count);
    }

    [Fact]
    public async Task Cancel_with_pending_upgrade_closes_the_upgrade_first()
    {
        var tenantId = Guid.NewGuid();
        var subscription = ActiveSubscription(tenantId, out var plan, out var version);
        var upgradeResult = subscription.RequestUpgrade(
            plan,
            version,
            BillingCycle.Yearly,
            chargeAmountCents: 10000,
            chargeCurrency: "USD",
            paymentIdempotencyKey: Guid.NewGuid().ToString("N"),
            Guid.Empty,
            DateTime.UtcNow
        );
        Assert.True(upgradeResult.IsSuccess);

        var repo = new FakeSubscriptionRepo(subscription);
        var cancellation = new CountingCancellationClient();
        var bus = new CapturingMessageBus();
        var audit = new FakeSubscriptionAuditLogWriter();

        var result = await CancelSubscriptionHandler.Handle(
            new CancelSubscriptionCommand(tenantId, "pulled the trigger too fast", Guid.NewGuid()),
            repo,
            cancellation,
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            audit,
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.All(
            subscription.PlanChangeRequests,
            r => Assert.NotEqual(PlanChangeRequestStatus.AwaitingPayment, r.Status)
        );
        Assert.Contains(bus.Published, m => m is PlanChangeUpgradeCancelledIntegrationEvent);
        Assert.Contains(bus.Published, m => m is TenantSubscriptionStatusChangedIntegrationEvent);
    }

    [Fact]
    public async Task Cancel_from_expired_fails_without_publishing()
    {
        var tenantId = Guid.NewGuid();
        var subscription = ActiveSubscription(tenantId, out _, out _);
        subscription.ScheduleCancellation("too expensive", Guid.Empty, DateTime.UtcNow);
        subscription.ExpireAfterScheduledCancellation(Guid.Empty, subscription.CurrentPeriodEndUtc);
        var repo = new FakeSubscriptionRepo(subscription);
        var bus = new CapturingMessageBus();
        var audit = new FakeSubscriptionAuditLogWriter();

        var result = await CancelSubscriptionHandler.Handle(
            new CancelSubscriptionCommand(tenantId, "retry", Guid.NewGuid()),
            repo,
            new CountingCancellationClient(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            audit,
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.InvalidTransition", result.Error.Code);
        Assert.Empty(bus.Published);
    }

    private static TenantSubscription ActiveSubscription(
        Guid tenantId,
        out SubscriptionPlan plan,
        out SubscriptionPlanVersion version
    )
    {
        var nowUtc = DateTime.UtcNow;
        var periodEndUtc = nowUtc.AddDays(30);
        plan = SubscriptionPlan
            .Create(PlanCode.Create("pro").Value, "pro", "pro plan", PlanTier.Starter, Guid.Empty, nowUtc)
            .Value;
        version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly, BillingCycle.Yearly]).Value;
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);

        return TenantSubscription
            .ActivateImmediately(
                tenantId,
                plan,
                version,
                BillingCycle.Monthly,
                nowUtc,
                periodEndUtc,
                Guid.Empty,
                nowUtc
            )
            .Value;
    }
}
