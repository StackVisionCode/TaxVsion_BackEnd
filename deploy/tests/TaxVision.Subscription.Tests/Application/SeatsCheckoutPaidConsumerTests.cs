using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Seats.IntegrationEvents;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>Al confirmarse el pago del checkout de asientos, el consumer aprovisiona los asientos (crea+activa)
/// sin volver a cobrar, marca la intención Provisioned y recalcula el cupo. Idempotente en redelivery.</summary>
public sealed class SeatsCheckoutPaidConsumerTests
{
    [Fact]
    public async Task Provisions_the_seats_and_marks_the_intent_provisioned()
    {
        var subscription = ActiveSubscription(out var tenantId);
        var intent = PendingIntent(tenantId, quantity: 2);
        var intents = new FakeSeatPurchaseIntentRepository(intent);
        var seatRepo = new CapturingSeatRepo();
        var bus = new CapturingMessageBus();

        await SeatsCheckoutPaidConsumer.Handle(
            PaidEvent(tenantId, intent.Id),
            intents,
            new FakeSubscriptionRepo(subscription),
            seatRepo,
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            new FakeSubscriptionMetrics(),
            NullLogger<SubscriptionSeat>.Instance,
            CancellationToken.None
        );

        Assert.Equal(2, seatRepo.Added.Count);
        Assert.All(seatRepo.Added, seat => Assert.Equal(SeatStatus.Active, seat.Status));
        Assert.All(seatRepo.Added, seat => Assert.Equal(15m, seat.UnitPrice.Amount)); // precio de renovación copiado
        Assert.Equal(SeatPurchaseIntentStatus.Provisioned, intent.Status);
        Assert.Single(bus.Published.OfType<RecalculateEntitlementsCommand>());
    }

    [Fact]
    public async Task Is_idempotent_on_redelivery()
    {
        var subscription = ActiveSubscription(out var tenantId);
        var intent = PendingIntent(tenantId, quantity: 2);
        var intents = new FakeSeatPurchaseIntentRepository(intent);
        var seatRepo = new CapturingSeatRepo();
        var evt = PaidEvent(tenantId, intent.Id);

        for (var i = 0; i < 2; i++)
            await SeatsCheckoutPaidConsumer.Handle(
                evt,
                intents,
                new FakeSubscriptionRepo(subscription),
                seatRepo,
                new FakeUnitOfWork(),
                new CapturingMessageBus(),
                new FakeCorrelationContext(),
                new FakeSubscriptionAuditLogWriter(),
                new FakeSubscriptionMetrics(),
                NullLogger<SubscriptionSeat>.Instance,
                CancellationToken.None
            );

        Assert.Equal(2, seatRepo.Added.Count); // no se duplican en la segunda entrega
        Assert.Equal(SeatPurchaseIntentStatus.Provisioned, intent.Status);
    }

    [Fact]
    public async Task Ignores_a_tenant_mismatch()
    {
        var subscription = ActiveSubscription(out var tenantId);
        var intent = PendingIntent(tenantId, quantity: 2);
        var seatRepo = new CapturingSeatRepo();

        await SeatsCheckoutPaidConsumer.Handle(
            PaidEvent(Guid.NewGuid(), intent.Id), // otro tenant
            new FakeSeatPurchaseIntentRepository(intent),
            new FakeSubscriptionRepo(subscription),
            seatRepo,
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            new FakeSubscriptionMetrics(),
            NullLogger<SubscriptionSeat>.Instance,
            CancellationToken.None
        );

        Assert.Empty(seatRepo.Added);
        Assert.Equal(SeatPurchaseIntentStatus.Pending, intent.Status);
    }

    private static SeatsCheckoutPaidIntegrationEvent PaidEvent(Guid tenantId, Guid intentId) =>
        new()
        {
            TenantId = tenantId,
            SeatPurchaseIntentId = intentId,
            SaaSPaymentId = Guid.NewGuid(),
            AmountPaidCents = 3000,
            Currency = "USD",
            PaidAtUtc = DateTime.UtcNow,
            ProviderPaymentReference = "pi_123",
        };

    private static SeatPurchaseIntent PendingIntent(Guid tenantId, int quantity) =>
        SeatPurchaseIntent
            .Create(
                tenantId,
                SeatType.Standard,
                quantity,
                autoRenew: true,
                Money.Create(15m, "USD").Value,
                BillingCycle.Monthly,
                proratedTotalCents: 3000,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

    private static TenantSubscription ActiveSubscription(out Guid tenantId)
    {
        var now = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("pro").Value, "pro", "pro plan", PlanTier.Standard, Guid.Empty, now)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, versionNumber: 1, trialDaysDefault: 14, [BillingCycle.Monthly, BillingCycle.Yearly])
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
        tenantId = subscription.TenantId;
        return subscription;
    }
}
