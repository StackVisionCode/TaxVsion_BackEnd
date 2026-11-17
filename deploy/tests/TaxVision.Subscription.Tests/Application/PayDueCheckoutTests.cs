using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.Commands.StartRenewalCheckout;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>"Pay what's due" por hosted-checkout: un pago con una línea por ítem; al pagarse vuelve cada uno.</summary>
public sealed class PayDueCheckoutTests
{
    [Fact]
    public async Task Paying_a_suspended_seat_asks_for_one_checkout_with_its_line()
    {
        var nowUtc = DateTime.UtcNow;
        var tenantId = Guid.NewGuid();
        var subscription = TestSubscriptions.Active(tenantId, nowUtc.AddDays(-20), nowUtc.AddDays(10), out var plan);
        var seat = SuspendedSeat(tenantId, nowUtc);
        var seats = new CapturingSeatRepo();
        seats.Added.Add(seat);
        var intents = new FakeRenewalCheckoutIntentRepository();

        var reserved = await ReserveRenewalCheckoutHandler.Handle(
            new ReserveRenewalCheckoutCommand(Command(tenantId, [seat.Id])),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            intents,
            seats,
            new FakeTenantAddOnRepo(),
            new FakeAddOnDefinitionRepository(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(reserved.IsSuccess, reserved.IsFailure ? reserved.Error.Code : null);
        var line = Assert.Single(reserved.Value.Lines!);
        Assert.Equal(("Seat", seat.Id), (line.TargetKind, line.TargetId!.Value));
        Assert.InRange(reserved.Value.AmountCents, 450, 550);
    }

    [Fact]
    public async Task A_paid_due_checkout_reactivates_the_seat_without_touching_the_plan()
    {
        var nowUtc = DateTime.UtcNow;
        var tenantId = Guid.NewGuid();
        var subscription = TestSubscriptions.Active(tenantId, nowUtc.AddDays(-20), nowUtc.AddDays(10));
        var planEnd = subscription.CurrentPeriodEndUtc;
        var seat = SuspendedSeat(tenantId, nowUtc);
        var seats = new CapturingSeatRepo();
        seats.Added.Add(seat);
        var intent = SubscriptionRenewalIntent
            .CreateForDue(
                tenantId,
                Money.FromCents(500, "USD"),
                BillingCycle.Monthly,
                includesPlan: false,
                [new SubscriptionRenewalIntentItem("Seat", seat.Id, 500)],
                Guid.NewGuid(),
                nowUtc
            )
            .Value;
        intent.AttachCheckout(Guid.NewGuid(), "https://pay/x", nowUtc.AddHours(1), nowUtc);
        var intents = new FakeRenewalCheckoutIntentRepository();
        await intents.AddAsync(intent);
        var bus = new CapturingMessageBus();

        await SubscriptionRenewalCheckoutPaidConsumer.Handle(
            new BuildingBlocks.Messaging.PaymentAppIntegrationEvents.SubscriptionRenewalCheckoutPaidIntegrationEvent
            {
                TenantId = tenantId,
                RenewalIntentId = intent.Id,
                SaaSPaymentId = Guid.NewGuid(),
                AmountPaidCents = 500,
                Currency = "USD",
                PaidAtUtc = nowUtc,
                ProviderPaymentReference = "pi_1",
            },
            intents,
            new FakeSubscriptionRepo(subscription),
            seats,
            new FakeTenantAddOnRepo(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            new FakeSubscriptionMetrics(),
            NullLogger<SubscriptionRenewalIntent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(SeatStatus.Active, seat.Status);
        Assert.Equal(planEnd, seat.CurrentPeriodEndUtc);
        Assert.Equal(planEnd, subscription.CurrentPeriodEndUtc);
        Assert.Equal(SubscriptionRenewalIntentStatus.Provisioned, intent.Status);
    }

    private static StartRenewalCheckoutCommand Command(Guid tenantId, IReadOnlyList<Guid> seatIds) =>
        new(
            tenantId,
            "owner@acme.test",
            "https://app/ok",
            "https://app/cancel",
            "Stripe",
            "Card",
            Guid.NewGuid(),
            SeatIds: seatIds,
            AddOnIds: []
        );

    private static SubscriptionSeat SuspendedSeat(Guid tenantId, DateTime nowUtc)
    {
        var seat = SubscriptionSeat
            .Purchase(
                tenantId,
                SeatType.Standard,
                SeatSourceType.Manual,
                null,
                Money.Create(15m, "USD").Value,
                BillingCycle.Monthly,
                autoRenew: true,
                Guid.Empty,
                nowUtc
            )
            .Value;
        seat.Activate(nowUtc.AddDays(-40), nowUtc.AddDays(-10), Guid.Empty, nowUtc);
        seat.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, nowUtc);
        seat.SuspendAfterRetriesExhausted(Guid.Empty, nowUtc);
        return seat;
    }
}
