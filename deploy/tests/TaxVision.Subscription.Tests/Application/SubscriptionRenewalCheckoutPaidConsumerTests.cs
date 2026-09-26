using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>Al confirmarse el pago del checkout de renovación, el consumer reactiva la suscripción (lapso →
/// Active con período nuevo) sin re-cobrar, marca la intención Provisioned, publica el status-changed con motivo
/// SelfServiceRenewed y recalcula entitlements. Idempotente en redelivery. Espejo de <c>SeatsCheckoutPaidConsumerTests</c>.</summary>
public sealed class SubscriptionRenewalCheckoutPaidConsumerTests
{
    [Fact]
    public async Task Reactivates_the_subscription_and_publishes_self_service_renewed()
    {
        var subscription = ExpiredSubscription(out var tenantId);
        var intent = PendingIntent(tenantId);
        var intents = new FakeRenewalCheckoutIntentRepository(intent);
        var bus = new CapturingMessageBus();
        var metrics = new FakeSubscriptionMetrics();

        await SubscriptionRenewalCheckoutPaidConsumer.Handle(
            PaidEvent(tenantId, intent.Id),
            intents,
            new FakeSubscriptionRepo(subscription),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            metrics,
            NullLogger<SubscriptionRenewalIntent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(SubscriptionRenewalIntentStatus.Provisioned, intent.Status);
        Assert.Single(bus.Published.OfType<RecalculateEntitlementsCommand>());
        Assert.Equal("succeeded", Assert.Single(metrics.SelfServiceRenewals));

        var statusChanged = Assert.Single(bus.Published.OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
        Assert.Equal("Active", statusChanged.Status);
        Assert.Equal(nameof(SubscriptionChangeReason.SelfServiceRenewed), statusChanged.Reason);
    }

    [Fact]
    public async Task Is_idempotent_on_redelivery()
    {
        var subscription = ExpiredSubscription(out var tenantId);
        var intent = PendingIntent(tenantId);
        var intents = new FakeRenewalCheckoutIntentRepository(intent);
        var evt = PaidEvent(tenantId, intent.Id);

        for (var i = 0; i < 2; i++)
            await SubscriptionRenewalCheckoutPaidConsumer.Handle(
                evt,
                intents,
                new FakeSubscriptionRepo(subscription),
                new FakeUnitOfWork(),
                new CapturingMessageBus(),
                new FakeCorrelationContext(),
                new FakeSubscriptionMetrics(),
                NullLogger<SubscriptionRenewalIntent>.Instance,
                CancellationToken.None
            );

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(SubscriptionRenewalIntentStatus.Provisioned, intent.Status);
    }

    [Fact]
    public async Task Ignores_a_tenant_mismatch()
    {
        var subscription = ExpiredSubscription(out var tenantId);
        var intent = PendingIntent(tenantId);

        await SubscriptionRenewalCheckoutPaidConsumer.Handle(
            PaidEvent(Guid.NewGuid(), intent.Id), // otro tenant
            new FakeRenewalCheckoutIntentRepository(intent),
            new FakeSubscriptionRepo(subscription),
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            new FakeCorrelationContext(),
            new FakeSubscriptionMetrics(),
            NullLogger<SubscriptionRenewalIntent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(SubscriptionStatus.Expired, subscription.Status);
        Assert.Equal(SubscriptionRenewalIntentStatus.Pending, intent.Status);
    }

    private static SubscriptionRenewalCheckoutPaidIntegrationEvent PaidEvent(Guid tenantId, Guid intentId) =>
        new()
        {
            TenantId = tenantId,
            RenewalIntentId = intentId,
            SaaSPaymentId = Guid.NewGuid(),
            AmountPaidCents = 4900,
            Currency = "USD",
            PaidAtUtc = DateTime.UtcNow,
            ProviderPaymentReference = "pi_123",
        };

    private static SubscriptionRenewalIntent PendingIntent(Guid tenantId)
    {
        var intent = SubscriptionRenewalIntent
            .Create(tenantId, 4900, "USD", BillingCycle.Monthly, Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        intent.AttachCheckout(Guid.NewGuid(), "https://pay/x", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);
        return intent;
    }

    private static TenantSubscription ExpiredSubscription(out Guid tenantId)
    {
        var now = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("pro").Value, "pro", "pro plan", PlanTier.Standard, Guid.Empty, now)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, versionNumber: 1, trialDaysDefault: 14, [BillingCycle.Monthly])
            .Value;
        plan.AddVersion(version, Guid.Empty, now);
        plan.PublishVersion(version.Id, now, Guid.Empty, now);
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                now,
                now.AddDays(30),
                Guid.Empty,
                now
            )
            .Value;
        subscription.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, now);
        subscription.EnterGracePeriodAfterRetriesExhausted(now.AddDays(7), Guid.Empty, now);
        subscription.SuspendBecauseGraceExpired(Guid.Empty, now.AddDays(8));
        subscription.ExpireAfterSuspensionTimeout(Guid.Empty, now.AddDays(40));
        tenantId = subscription.TenantId;
        return subscription;
    }
}
