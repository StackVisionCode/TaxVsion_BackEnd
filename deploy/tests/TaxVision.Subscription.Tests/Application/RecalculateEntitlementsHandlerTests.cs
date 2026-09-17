using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// The seat math behind the effective staff-user cap published on
/// <c>TenantEntitlementsChangedIntegrationEvent.MaxStaffUsers</c> (= plan <c>seats.max</c> included +
/// purchased STAFF seats). Only <see cref="SeatType.Standard"/> seats in a non-terminal status add to the
/// tenant's user cap — other seat pools (Portal/Signature/ReadOnly/ServiceAccount) never do, so a client
/// (portal) seat can't silently raise the staff cap.
/// </summary>
public sealed class RecalculateEntitlementsHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc);

    private static SubscriptionSeat Seat(SeatType type) =>
        SubscriptionSeat
            .Purchase(
                Tenant,
                type,
                SeatSourceType.Manual,
                null,
                Money.Zero("USD"),
                BillingCycle.Monthly,
                false,
                Actor,
                Now
            )
            .Value;

    [Fact]
    public void CountActiveStaffSeats_counts_only_non_terminal_standard_seats()
    {
        var cancelledStandard = Seat(SeatType.Standard);
        Assert.True(cancelledStandard.CancelBeforeActivation("test", Actor, Now).IsSuccess);

        var seats = new List<SubscriptionSeat>
        {
            Seat(SeatType.Standard), // counts
            Seat(SeatType.Standard), // counts
            Seat(SeatType.Portal), // client pool — never counts against staff
            Seat(SeatType.Signature), // separate pool — never counts
            cancelledStandard, // terminal — excluded
        };

        Assert.Equal(2, RecalculateEntitlementsHandler.CountActiveStaffSeats(seats));
    }

    [Fact]
    public void CountActiveStaffSeats_is_zero_when_there_are_no_seats()
    {
        Assert.Equal(0, RecalculateEntitlementsHandler.CountActiveStaffSeats([]));
    }
}
