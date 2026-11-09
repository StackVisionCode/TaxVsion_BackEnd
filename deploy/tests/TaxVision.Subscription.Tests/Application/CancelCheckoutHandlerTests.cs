using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns.Commands.CancelAddOnCheckout;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Seats.Commands.CancelSeatCheckout;
using TaxVision.Subscription.Application.Subscriptions.Commands.CancelRenewalCheckout;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

public sealed class CancelCheckoutHandlerTests
{
    private static SeatPurchaseIntent PendingSeat(Guid tenantId, bool withPayment = true)
    {
        var intent = SeatPurchaseIntent
            .Create(
                tenantId,
                SeatType.Standard,
                quantity: 2,
                autoRenew: true,
                Money.Create(15m, "USD").Value,
                BillingCycle.Monthly,
                proratedTotal: Money.FromCents(3000, "USD"),
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        if (withPayment)
            intent.AttachCheckout(Guid.NewGuid(), "https://pay", DateTime.UtcNow.AddHours(1), DateTime.UtcNow);
        return intent;
    }

    [Fact]
    public async Task Seat_cancel_marks_cancelled_calls_provider_and_publishes_event()
    {
        var tenantId = Guid.NewGuid();
        var intent = PendingSeat(tenantId);
        var repo = new FakeSeatPurchaseIntentRepository(intent);
        var bus = new CapturingMessageBus();
        var uow = new FakeUnitOfWork();
        var audit = new FakeSubscriptionAuditLogWriter();

        var result = await CancelSeatCheckoutHandler.Handle(
            new CancelSeatCheckoutCommand(tenantId, intent.Id, Guid.NewGuid(), "closed window"),
            repo,
            uow,
            bus,
            new FakeCorrelationContext(),
            audit,
            NullLogger<SeatPurchaseIntent>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AlreadyCancelled);
        Assert.Equal(SeatPurchaseIntentStatus.Cancelled, intent.Status);
        var providerCancel = Assert.Single(bus.Published.OfType<CancelHostedCheckoutCommand>());
        Assert.Equal(intent.SaaSPaymentId, providerCancel.SaaSPaymentId);
        Assert.Contains(bus.Published, m => m is SeatCheckoutCancelledIntegrationEvent);
    }

    [Fact]
    public async Task Seat_cancel_from_another_tenant_returns_not_found()
    {
        var intent = PendingSeat(Guid.NewGuid());
        var repo = new FakeSeatPurchaseIntentRepository(intent);

        var result = await CancelSeatCheckoutHandler.Handle(
            new CancelSeatCheckoutCommand(Guid.NewGuid(), intent.Id, Guid.NewGuid(), null),
            repo,
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            NullLogger<SeatPurchaseIntent>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SeatPurchaseIntent.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Seat_cancel_rejects_a_paid_intent()
    {
        var tenantId = Guid.NewGuid();
        var intent = PendingSeat(tenantId);
        intent.MarkPaid(DateTime.UtcNow);
        var repo = new FakeSeatPurchaseIntentRepository(intent);

        var result = await CancelSeatCheckoutHandler.Handle(
            new CancelSeatCheckoutCommand(tenantId, intent.Id, Guid.NewGuid(), null),
            repo,
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            NullLogger<SeatPurchaseIntent>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SeatPurchaseIntent.AlreadyPaid", result.Error.Code);
    }

    [Fact]
    public async Task Seat_cancel_twice_returns_already_cancelled_without_republishing()
    {
        var tenantId = Guid.NewGuid();
        var intent = PendingSeat(tenantId);
        var repo = new FakeSeatPurchaseIntentRepository(intent);
        var bus = new CapturingMessageBus();

        await CancelSeatCheckoutHandler.Handle(
            new CancelSeatCheckoutCommand(tenantId, intent.Id, Guid.NewGuid(), null),
            repo,
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            NullLogger<SeatPurchaseIntent>.Instance,
            CancellationToken.None
        );

        var secondBus = new CapturingMessageBus();
        var second = await CancelSeatCheckoutHandler.Handle(
            new CancelSeatCheckoutCommand(tenantId, intent.Id, Guid.NewGuid(), null),
            repo,
            new FakeUnitOfWork(),
            secondBus,
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            NullLogger<SeatPurchaseIntent>.Instance,
            CancellationToken.None
        );

        Assert.True(second.IsSuccess);
        Assert.True(second.Value.AlreadyCancelled);
        Assert.Empty(secondBus.Published);
    }
}
