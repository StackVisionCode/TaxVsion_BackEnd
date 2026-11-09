using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Subscriptions.Commands.Cancel;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

public sealed class CancelSubscriptionHandlerTests
{
    [Fact]
    public async Task First_cancel_publishes_event_and_audits()
    {
        var tenantId = Guid.NewGuid();
        var subscription = ActiveSubscription(tenantId, out _, out _);
        var repo = new FakeSubscriptionRepo(subscription);
        var bus = new CapturingMessageBus();
        var audit = new FakeSubscriptionAuditLogWriter();

        var result = await CancelSubscriptionHandler.Handle(
            new CancelSubscriptionCommand(tenantId, "too expensive", Guid.NewGuid()),
            repo,
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
        Assert.Contains(
            SubscriptionDomainEventReplay.Emitted(bus, subscription),
            m => m is TenantSubscriptionStatusChangedIntegrationEvent
        );
    }

    [Fact]
    public async Task Second_cancel_is_idempotent_and_does_not_republish()
    {
        var tenantId = Guid.NewGuid();
        var subscription = ActiveSubscription(tenantId, out _, out _);
        var repo = new FakeSubscriptionRepo(subscription);
        var bus = new CapturingMessageBus();
        var audit = new FakeSubscriptionAuditLogWriter();

        await CancelSubscriptionHandler.Handle(
            new CancelSubscriptionCommand(tenantId, "too expensive", Guid.NewGuid()),
            repo,
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
        var bus = new CapturingMessageBus();
        var audit = new FakeSubscriptionAuditLogWriter();

        var result = await CancelSubscriptionHandler.Handle(
            new CancelSubscriptionCommand(tenantId, "pulled the trigger too fast", Guid.NewGuid()),
            repo,
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
        Assert.Contains(
            SubscriptionDomainEventReplay.Emitted(bus, subscription),
            m => m is TenantSubscriptionStatusChangedIntegrationEvent
        );
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

    // BB-14: por InvokeAsync, lo publicado antes de un guardado fallido sale igual tras el reintento en línea.
    [Fact]
    public async Task Cancel_with_pending_upgrade_publishes_nothing_when_the_save_fails()
    {
        var tenantId = Guid.NewGuid();
        var subscription = WithPendingCheckout(tenantId);
        var bus = new CapturingMessageBus();

        await Assert.ThrowsAsync<TimeoutException>(
            () =>
                CancelSubscriptionHandler.Handle(
                    new CancelSubscriptionCommand(tenantId, "too expensive", Guid.NewGuid()),
                    new FakeSubscriptionRepo(subscription),
                    new FailingUnitOfWork(),
                    bus,
                    new FakeCorrelationContext(),
                    new FakeSubscriptionAuditLogWriter(),
                    NullLogger<TenantSubscription>.Instance,
                    CancellationToken.None
                )
        );

        Assert.Empty(bus.Published);
    }

    // El orden del cobro no cambia: primero se avisa del upgrade cancelado, después se cierra el checkout.
    [Fact]
    public async Task Cancel_with_pending_upgrade_keeps_the_order_of_the_charge_messages()
    {
        var tenantId = Guid.NewGuid();
        var subscription = WithPendingCheckout(tenantId);
        var bus = new CapturingMessageBus();

        var result = await CancelSubscriptionHandler.Handle(
            new CancelSubscriptionCommand(tenantId, "too expensive", Guid.NewGuid()),
            new FakeSubscriptionRepo(subscription),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Collection(
            bus.Published,
            message => Assert.IsType<PlanChangeUpgradeCancelledIntegrationEvent>(message),
            message => Assert.IsType<CancelHostedCheckoutCommand>(message)
        );
    }

    private static TenantSubscription WithPendingCheckout(Guid tenantId)
    {
        var subscription = ActiveSubscription(tenantId, out var plan, out var version);
        var requested = subscription.RequestUpgrade(
            plan,
            version,
            BillingCycle.Yearly,
            chargeAmountCents: 10000,
            chargeCurrency: "USD",
            paymentIdempotencyKey: Guid.NewGuid().ToString("N"),
            Guid.Empty,
            DateTime.UtcNow
        );
        Assert.True(requested.IsSuccess);
        var attached = subscription.AttachUpgradeCheckout(
            subscription.PlanChangeRequests.Single().Id,
            Guid.NewGuid(),
            "https://checkout.test/upgrade",
            DateTime.UtcNow.AddHours(1),
            Guid.Empty,
            DateTime.UtcNow
        );
        Assert.True(attached.IsSuccess);
        return subscription;
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
