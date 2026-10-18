using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Seats.IntegrationEvents;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

public sealed class SeatRenewalPaymentConsumerTests
{
    [Fact]
    public async Task Final_payment_failure_suspends_seat_and_recalculates_entitlements()
    {
        var (seat, key) = ActiveSeatWithRenewal();
        var bus = new CapturingMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        await SeatRenewalPaymentFailedConsumer.Handle(
            new SeatRenewalPaymentFailedIntegrationEvent
            {
                TenantId = seat.TenantId,
                SeatId = seat.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = key,
                FailureCode = "card_declined",
                FailureReason = "Card declined",
                WillRetry = false,
            },
            new SeatRepository(seat),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            NullLogger<SubscriptionSeat>.Instance,
            CancellationToken.None
        );

        Assert.Equal(SeatStatus.Suspended, seat.Status);
        Assert.Equal("RenewalFailed", seat.SuspensionReason);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Single(bus.Published.OfType<SubscriptionSeatSuspendedIntegrationEvent>());
        Assert.Single(bus.Published.OfType<RecalculateEntitlementsCommand>());
    }

    [Fact]
    public async Task Successful_reactivation_unlocks_capacity_and_recalculates_entitlements()
    {
        var (seat, key) = SuspendedSeatWithReactivation();
        var bus = new CapturingMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        await SeatRenewalPaymentSucceededConsumer.Handle(
            new SeatRenewalPaymentSucceededIntegrationEvent
            {
                TenantId = seat.TenantId,
                SeatId = seat.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = key,
                ExternalPaymentReference = "payment-1",
                PaidAtUtc = DateTime.UtcNow,
            },
            new SeatRepository(seat),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            NullLogger<SubscriptionSeat>.Instance,
            CancellationToken.None
        );

        Assert.Equal(SeatStatus.Active, seat.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Single(bus.Published.OfType<SubscriptionSeatReactivatedIntegrationEvent>());
        Assert.Single(bus.Published.OfType<RecalculateEntitlementsCommand>());
    }

    private static (SubscriptionSeat Seat, string Key) ActiveSeatWithRenewal()
    {
        var nowUtc = DateTime.UtcNow;
        var seat = CreateActiveSeat(nowUtc);
        const string key = "seat-renewal-1";
        Assert.True(seat.BeginRenewal(key, Guid.Empty, nowUtc).IsSuccess);
        return (seat, key);
    }

    private static (SubscriptionSeat Seat, string Key) SuspendedSeatWithReactivation()
    {
        var nowUtc = DateTime.UtcNow;
        var seat = CreateActiveSeat(nowUtc);
        Assert.True(seat.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, nowUtc).IsSuccess);
        Assert.True(seat.SuspendAfterRetriesExhausted(Guid.Empty, nowUtc).IsSuccess);

        const string key = "seat-reactivation-1";
        Assert.True(seat.BeginReactivation(key, nowUtc.AddMonths(1), Guid.Empty, nowUtc).IsSuccess);
        return (seat, key);
    }

    private static SubscriptionSeat CreateActiveSeat(DateTime nowUtc)
    {
        var seat = SubscriptionSeat
            .Purchase(
                Guid.NewGuid(),
                SeatType.Standard,
                SeatSourceType.Plan,
                null,
                Money.Create(15m, "USD").Value,
                BillingCycle.Monthly,
                true,
                Guid.Empty,
                nowUtc
            )
            .Value;
        Assert.True(seat.Activate(nowUtc, nowUtc.AddMonths(1), Guid.Empty, nowUtc).IsSuccess);
        return seat;
    }

    private sealed class SeatRepository(SubscriptionSeat seat) : ISubscriptionSeatRepository
    {
        public Task<SubscriptionSeat?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(id == seat.Id && tenantId == seat.TenantId ? seat : null);

        public Task<IReadOnlyList<SubscriptionSeat>> GetByTenantIdAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(SubscriptionSeat value, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SubscriptionSeat>> GetDueForRenewalAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SubscriptionSeat>> GetSuspendedBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SubscriptionSeat>> GetCancelledPastPeriodEndAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SubscriptionSeat>> GetRenewingBetweenAsync(
            DateTime fromUtc,
            DateTime toUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<(IReadOnlyList<SubscriptionSeat> Items, int TotalCount)> GetExpiredAsync(
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
