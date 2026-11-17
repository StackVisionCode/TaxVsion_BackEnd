using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Application.Subscriptions.Renewals;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>El resultado del cobro del paquete se aplica al plan y a cada asiento y add-on que iba en él.</summary>
public sealed class BundledRenewalConsumersTests
{
    private const string PlanKey = "subscription-renewal-bundle-1";

    [Fact]
    public async Task A_paid_bundle_renews_the_plan_and_its_seats()
    {
        var (subscription, seat) = Bundle();
        var seats = new CapturingSeatRepo();
        seats.Added.Add(seat);
        var oldEnd = seat.CurrentPeriodEndUtc;

        await SubscriptionRenewalPaymentSucceededConsumer.Handle(
            new SubscriptionRenewalPaymentSucceededIntegrationEvent
            {
                TenantId = subscription.TenantId,
                TenantSubscriptionId = subscription.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = PlanKey,
                ExternalPaymentReference = "pi_1",
                PaidAtUtc = DateTime.UtcNow,
            },
            new FakeSubscriptionRepo(subscription),
            seats,
            new FakeTenantAddOnRepo(),
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            new FakeCorrelationContext(),
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.Equal(subscription.CurrentPeriodEndUtc, seat.CurrentPeriodEndUtc);
        Assert.True(seat.CurrentPeriodEndUtc > oldEnd);
    }

    [Fact]
    public async Task A_bundle_out_of_retries_suspends_its_seats_without_one_email_per_seat()
    {
        var (subscription, seat) = Bundle();
        var seats = new CapturingSeatRepo();
        seats.Added.Add(seat);
        var bus = new CapturingMessageBus();

        await SubscriptionRenewalPaymentFailedConsumer.Handle(
            new SubscriptionRenewalPaymentFailedIntegrationEvent
            {
                TenantId = subscription.TenantId,
                TenantSubscriptionId = subscription.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = PlanKey,
                FailureCode = "card_declined",
                FailureReason = "Card declined",
                WillRetry = false,
            },
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(null),
            seats,
            new FakeTenantAddOnRepo(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.Equal(SeatStatus.Suspended, seat.Status);
        Assert.Contains(bus.Published, message => message is SubscriptionSeatSuspendedIntegrationEvent);
        Assert.DoesNotContain(bus.Published, message => message is SeatRenewalPaymentFailedIntegrationEvent);
    }

    [Fact]
    public async Task The_failed_charge_email_names_the_whole_bundle()
    {
        var (subscription, seat) = Bundle();
        var seats = new CapturingSeatRepo();
        seats.Added.Add(seat);
        var bus = new CapturingMessageBus();

        await SubscriptionRenewalPaymentFailedConsumer.Handle(
            new SubscriptionRenewalPaymentFailedIntegrationEvent
            {
                TenantId = subscription.TenantId,
                TenantSubscriptionId = subscription.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = PlanKey,
                FailureCode = "card_declined",
                FailureReason = "Card declined",
                WillRetry = true,
                NextRetryAtUtc = DateTime.UtcNow.AddHours(6),
                IsAutomaticCharge = true,
                AmountCents = 4_400,
                Currency = "USD",
            },
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(null),
            seats,
            new FakeTenantAddOnRepo(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        var email = Assert.Single(bus.Published.OfType<RenewalChargeFailedIntegrationEvent>());
        Assert.Equal("your subscription and 1 seat", email.ItemDescription);
    }

    private static (TenantSubscription Subscription, SubscriptionSeat Seat) Bundle()
    {
        var nowUtc = DateTime.UtcNow;
        var subscription = TestSubscriptions.Active(Guid.NewGuid(), nowUtc.AddMonths(-1), nowUtc);
        subscription.BeginRenewal(PlanKey, Guid.Empty, nowUtc);
        var seat = SubscriptionSeat
            .Purchase(
                subscription.TenantId,
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
        seat.Activate(subscription.CurrentPeriodStartUtc, subscription.CurrentPeriodEndUtc, Guid.Empty, nowUtc);
        RenewalBundle.Begin(subscription, PlanKey, [seat], [], new Dictionary<string, string>(), nowUtc);
        Assert.Single(seat.Renewals);
        return (subscription, seat);
    }
}
