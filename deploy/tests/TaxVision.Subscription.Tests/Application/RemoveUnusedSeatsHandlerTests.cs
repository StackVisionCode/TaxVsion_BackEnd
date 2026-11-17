using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Seats.Commands.RemoveUnusedSeats;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>D-C: los asientos suspendidos que no se van a pagar se quitan de una, sin esperar los 30 días.</summary>
public sealed class RemoveUnusedSeatsHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Removing_suspended_seats_ends_them_and_stops_their_retries()
    {
        var seat = SuspendedSeat();
        var seats = new CapturingSeatRepo();
        seats.Added.Add(seat);
        var bus = new CapturingMessageBus();

        var result = await RemoveAsync(seats, bus, [seat.Id]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, result.Value.Removed);
        Assert.Equal(SeatStatus.Expired, seat.Status);
        var superseded = Assert.Single(bus.Published.OfType<RenewalChargesSupersededIntegrationEvent>());
        Assert.Contains("seat-renewal-x", superseded.IdempotencyKeys);
    }

    [Fact]
    public async Task An_active_seat_cannot_be_removed_here()
    {
        var active = ActiveSeat();
        var seats = new CapturingSeatRepo();
        seats.Added.Add(active);

        var result = await RemoveAsync(seats, new CapturingMessageBus(), [active.Id]);

        Assert.Equal("Seat.NotRemovable", result.Error.Code);
        Assert.Equal(SeatStatus.Active, active.Status);
    }

    private static Task<BuildingBlocks.Results.Result<RemoveUnusedSeatsResponse>> RemoveAsync(
        CapturingSeatRepo seats,
        CapturingMessageBus bus,
        IReadOnlyList<Guid> seatIds
    ) =>
        RemoveUnusedSeatsHandler.Handle(
            new RemoveUnusedSeatsCommand(TenantId, seatIds, Guid.NewGuid()),
            seats,
            new FakeUnitOfWork(),
            bus,
            NullLogger<SubscriptionSeat>.Instance,
            CancellationToken.None
        );

    private static SubscriptionSeat ActiveSeat()
    {
        var nowUtc = DateTime.UtcNow;
        var seat = SubscriptionSeat
            .Purchase(
                TenantId,
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
        return seat;
    }

    private static SubscriptionSeat SuspendedSeat()
    {
        var nowUtc = DateTime.UtcNow;
        var seat = ActiveSeat();
        seat.BeginRenewal("seat-renewal-x", Guid.Empty, nowUtc);
        seat.FailRenewal(
            seat.Renewals.First().Id,
            "card_declined",
            "Declined",
            true,
            nowUtc.AddHours(6),
            Guid.Empty,
            nowUtc
        );
        seat.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, nowUtc);
        seat.SuspendAfterRetriesExhausted(Guid.Empty, nowUtc);
        return seat;
    }
}
